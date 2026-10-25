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
        public static ThingDef YDM_MineShaftDeep;
        public static ThingDef YDM_MineShaftExit;
        public static MapGeneratorDef YDM_Underground;
        public static DamageDef YDM_CaveIn;
        public static DutyDef YDM_EnterShaft;
        public static JobDef YDM_HaulUpShaft;

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
            return def == YDMDefOf.YDM_MineShaft || def == YDMDefOf.YDM_MineShaftDeep;
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

            foreach (Thing thing in map.listerThings.ThingsOfDef(YDMDefOf.YDM_MineShaftDeep))
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
