using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace YuudongDeepMining
{
    // Better Workbench Management('나가 있는 림도 세기')와 함께 쓸 때:
    // 그 모드는 정착지 지도에서 사라진 림에게 출신 지도를 적어 두고, 작업대 개수를 셀 때 출신 지도의 개수에 그 림의 소지품·장착품을 더한다.
    // 갱도로 내려간 림도 '나간 림'이 되는데, 이 모드는 층 묶음 전체를 이미 세므로 같은 림이 두 번 세진다.
    // 같은 갱도 줄의 층에 들어선 림은 출신 지도를 지운다 (그 모드가 정착지로 돌아온 림에게 하는 것과 같은 처리).
    // 그 모드가 없으면 아무것도 하지 않는다.
    public static class WorkbenchCompat
    {
        private static readonly System.Type CompType = AccessTools.TypeByName("ImprovedWorkbenches.CompPawnOriginalMap");

        private static readonly FieldInfo OriginField = CompType == null ? null : AccessTools.Field(CompType, "OriginMap");

        private static readonly MethodInfo SetOrigin = CompType == null ? null : AccessTools.Method(CompType, "SetOriginMap", new[] { typeof(Map) });

        public static bool Present => OriginField != null;

        public static void ClearIfSameChain(Pawn pawn, Map map)
        {
            if (!Present || map == null || !(pawn is ThingWithComps twc) || twc.AllComps == null)
            {
                return;
            }

            foreach (ThingComp comp in twc.AllComps)
            {
                if (!CompType.IsInstanceOfType(comp))
                {
                    continue;
                }

                if (OriginField.GetValue(comp) is Map origin && origin != map && LayerUtility.SameChain(origin, map))
                {
                    if (SetOrigin != null)
                    {
                        SetOrigin.Invoke(comp, new object[] { null });
                    }
                    else
                    {
                        OriginField.SetValue(comp, null);
                    }

                    LayerDebug.Log(pawn, "Better Workbench origin map cleared (moved within the shaft chain)");
                }

                return;
            }
        }

        // 불러온 저장에서 이미 다른 층에 있는 림 (예전 버전에서 내려간 림).
        public static void ClearAll()
        {
            if (!Present)
            {
                return;
            }

            foreach (Map map in Find.Maps)
            {
                foreach (Pawn pawn in new List<Pawn>(map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer)))
                {
                    ClearIfSameChain(pawn, map);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    internal static class Pawn_SpawnSetup_WorkbenchCompat_Patch
    {
        private static void Postfix(Pawn __instance, Map map, bool respawningAfterLoad)
        {
            if (!respawningAfterLoad && WorkbenchCompat.Present && __instance.Faction == Faction.OfPlayer)
            {
                WorkbenchCompat.ClearIfSameChain(__instance, map);
            }
        }
    }
}
