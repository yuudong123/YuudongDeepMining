using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace YuudongDeepMining
{
    [DefOf]
    public static class YDMDefOf
    {
        public static ThingDef YDM_MineShaft;
        public static ThingDef YDM_SealedShaft;
        public static ThingDef YDM_MineShaftExit;
        public static MapGeneratorDef YDM_Underground;
        public static DamageDef YDM_CaveIn;
        public static DutyDef YDM_EnterShaft;
        public static JobDef YDM_HaulAcrossLayers;
        public static JobDef YDM_CarryThroughPortal;
        public static JobDef YDM_PlaceCarried;
        public static KeyBindingDef YDM_LayerUp;
        public static KeyBindingDef YDM_LayerDown;

        static YDMDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(YDMDefOf));
        }
    }

    // 층(지도) 사이 관계를 찾는 도우미. 지상은 깊이 0, 지하 n층은 깊이 n.
    public static class LayerUtility
    {
        // 지금 만들고 있는 지하 층의 깊이 (지도 생성 단계에서 씀).
        public static int generatingDepth;

        public static DeepMiningSettings Settings => DeepMiningMod.Settings;

        public static MapComponent_DeepLayer LayerComp(Map map)
        {
            return map?.GetComponent<MapComponent_DeepLayer>();
        }

        public static bool IsLayer(Map map)
        {
            return LayerComp(map) != null;
        }

        public static int DepthOf(Map map)
        {
            return LayerComp(map)?.depth ?? 0;
        }

        public static bool IsShaftDef(ThingDef def)
        {
            return def == YDMDefOf.YDM_MineShaft;
        }

        // 이 지도에서 아래층으로 내려가는 갱도 (지도마다 하나).
        public static Building_MineShaft DownShaft(Map map)
        {
            if (map == null)
            {
                return null;
            }

            foreach (Thing thing in map.listerThings.ThingsOfDef(YDMDefOf.YDM_MineShaft))
            {
                return (Building_MineShaft)thing;
            }

            return null;
        }

        // 지하 층의 위로 가는 출구.
        public static Building_MineShaftExit UpExit(Map map)
        {
            if (map == null || !IsLayer(map))
            {
                return null;
            }

            foreach (Thing thing in map.listerThings.ThingsOfDef(YDMDefOf.YDM_MineShaftExit))
            {
                return (Building_MineShaftExit)thing;
            }

            return null;
        }

        public static Map MapBelow(Map map)
        {
            return DownShaft(map)?.PocketMap;
        }

        public static Map MapAbove(Map map)
        {
            return UpExit(map)?.entrance?.Map;
        }

        // 이 지도 아래(바로 아래부터 맨 아래까지) 지하 층들.
        public static IEnumerable<Map> MapsBelow(Map map)
        {
            Map below = MapBelow(map);
            int guard = 0;
            while (below != null && guard++ < 64)
            {
                yield return below;
                below = MapBelow(below);
            }
        }

        public static int DeepestDepthBelow(Map map)
        {
            int depth = DepthOf(map);
            foreach (Map below in MapsBelow(map))
            {
                depth = Mathf.Max(depth, DepthOf(below));
            }

            return depth;
        }

        // 갱도로 이어진 줄의 맨 위 지도 (지상).
        public static Map Root(Map map)
        {
            int guard = 0;
            while (map != null && guard++ < 64)
            {
                Map above = MapAbove(map);
                if (above == null)
                {
                    return map;
                }

                map = above;
            }

            return map;
        }

        // 지상부터 맨 아래까지, 갱도로 이어진 지도들.
        public static List<Map> Chain(Map map)
        {
            List<Map> list = new List<Map>();
            Map root = Root(map);
            if (root == null)
            {
                return list;
            }

            list.Add(root);
            list.AddRange(MapsBelow(root));
            return list;
        }

        public static bool SameChain(Map a, Map b)
        {
            return a != null && b != null && (a == b || Root(a) == Root(b));
        }

        // a에서 b로 가려면 지금 지도에서 들어가야 할 포털 (위로 가면 사다리, 아래로 가면 갱도).
        public static MapPortal NextPortalToward(Map from, Map to)
        {
            if (from == null || to == null || from == to || !SameChain(from, to))
            {
                return null;
            }

            if (DepthOf(to) < DepthOf(from))
            {
                return UpExit(from);
            }

            Building_MineShaft shaft = DownShaft(from);
            return shaft != null && shaft.PocketMapExists ? shaft : null;
        }

        // 포털을 지나 도착하는 칸 (다른 지도 쪽).
        public static IntVec3 ArrivalCell(MapPortal portal, out Map otherMap)
        {
            otherMap = null;
            if (portal == null)
            {
                return IntVec3.Invalid;
            }

            otherMap = portal is Building_MineShaft shaft ? shaft.PocketMap : portal.GetOtherMap();
            if (otherMap == null)
            {
                return IntVec3.Invalid;
            }

            IntVec3 cell = portal.GetDestinationLocation();
            if (!cell.IsValid || !cell.InBounds(otherMap))
            {
                return IntVec3.Invalid;
            }

            return cell.Standable(otherMap) ? cell : CellFinder.StandableCellNear(cell, otherMap, 5f);
        }

        // from 지도에서 출발해 target 지도에 도착하는 칸 (마지막 포털 기준).
        public static IntVec3 ArrivalCellOn(Map from, Map target)
        {
            if (!SameChain(from, target) || from == target)
            {
                return IntVec3.Invalid;
            }

            // 위에서 내려오면 target 바로 위층의 갱도를, 아래에서 올라오면 target 바로 아래층의 사다리를 마지막으로 지난다.
            MapPortal last;
            if (DepthOf(from) < DepthOf(target))
            {
                Map above = MapAbove(target);
                last = above != null ? DownShaft(above) : null;
            }
            else
            {
                Map below = MapBelow(target);
                last = below != null ? UpExit(below) : null;
            }

            return ArrivalCell(last, out Map _);
        }

        public static bool AnyActiveColonist(Map map)
        {
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if (!pawn.Downed && !pawn.Dead)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool ColonistsBelow(Map map)
        {
            foreach (Map below in MapsBelow(map))
            {
                if (AnyActiveColonist(below))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool AnyLayerExists()
        {
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                if (IsLayer(maps[i]))
                {
                    return true;
                }
            }

            return false;
        }

        public static int DeepestLayerInGame()
        {
            int depth = 0;
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                depth = Mathf.Max(depth, DepthOf(maps[i]));
            }

            return depth;
        }

        // 바위·광석이 받는 피해를 나누는 값. 1이면 지상과 같음.
        public static float HardnessAt(int depth)
        {
            if (depth <= 0)
            {
                return 1f;
            }

            int capped = Mathf.Min(depth, Mathf.Max(1, Settings.hardnessCapDepth));
            return 1f + Settings.hardnessPerLayer * capped;
        }

        public static float InfestationMtbDays(int depth)
        {
            int capped = Mathf.Clamp(depth, 1, Mathf.Max(1, Settings.infestationCapDepth));
            return Settings.infestationBaseMtbDays * Mathf.Pow(Settings.infestationLayerFactor, capped - 1);
        }

        public static string LayerLabel(int depth)
        {
            return depth <= 0 ? "YDM.Surface".Translate().ToString() : "YDM.LayerN".Translate(depth).ToString();
        }
    }
}
