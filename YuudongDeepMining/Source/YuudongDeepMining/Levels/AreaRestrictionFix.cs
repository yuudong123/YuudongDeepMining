using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace YuudongDeepMining
{
    // 활동 구역: 바닐라 구역 칸은 '보고 있는 지도'의 구역을 보여 주고, 고른 구역을 그 림이 '있는 지도' 몫으로 저장한다.
    // 하단 탭이 다른 층 림도 보여 주므로, 그 줄에서는 림이 있는 층의 구역으로 그린다.
    // (다른 지도의 구역이 저장되면 그 층 길찾기가 '없는 구역'을 찾다 매 틱 실패한다.)
    [HarmonyPatch(typeof(PawnColumnWorker_AllowedArea), nameof(PawnColumnWorker_AllowedArea.DoCell))]
    internal static class PawnColumnWorker_AllowedArea_DoCell_Patch
    {
        private static void Prefix(Pawn pawn, out int __state)
        {
            Map map = pawn?.MapHeld;
            __state = map != null && map != Find.CurrentMap ? GhostUtility.SwapCurrentMap(map) : GhostUtility.NotSwapped;
        }

        private static void Finalizer(int __state)
        {
            GhostUtility.RestoreCurrentMap(__state);
        }
    }

    // 이미 다른 지도의 구역이 저장된 림을 고친다 (불러올 때, 그리고 게임 시간 1시간마다).
    // 거주 구역이었으면 그 층의 거주 구역으로, 다른 구역이었으면 제한 없음으로.
    public static class AreaRestrictionRepair
    {
        private static readonly AccessTools.FieldRef<Pawn_PlayerSettings, Dictionary<Map, Area>> AllowedAreas =
            AccessTools.FieldRefAccess<Pawn_PlayerSettings, Dictionary<Map, Area>>("allowedAreas");

        private static readonly List<KeyValuePair<Map, Area>> tmp = new List<KeyValuePair<Map, Area>>();

        public static void RepairAll()
        {
            int fixedCount = 0;
            foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_Alive)
            {
                fixedCount += Repair(pawn);
            }

            if (fixedCount > 0)
            {
                Log.Message("[YuudongDeepMining] 다른 층 구역으로 잘못 저장된 활동 구역 " + fixedCount + "개를 고쳤습니다.");
            }
        }

        private static int Repair(Pawn pawn)
        {
            Dictionary<Map, Area> areas = pawn.playerSettings == null ? null : AllowedAreas(pawn.playerSettings);
            if (areas == null || areas.Count == 0)
            {
                return 0;
            }

            tmp.Clear();
            foreach (KeyValuePair<Map, Area> kv in areas)
            {
                if (kv.Key != null && kv.Value != null && kv.Value.Map != kv.Key)
                {
                    tmp.Add(kv);
                }
            }

            foreach (KeyValuePair<Map, Area> kv in tmp)
            {
                Area replacement = kv.Value is Area_Home && !kv.Key.Disposed ? kv.Key.areaManager.Home : null;
                if (replacement != null)
                {
                    areas[kv.Key] = replacement;
                }
                else
                {
                    areas.Remove(kv.Key);
                }

                // 이미 잘못된 구역으로 낸 길찾기 요청을 버리고 다시 판단하게 한다.
                if (pawn.Spawned && pawn.Map == kv.Key)
                {
                    pawn.pather?.StopDead();
                    if (pawn.jobs?.curJob != null)
                    {
                        pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                    }
                }
            }

            int count = tmp.Count;
            tmp.Clear();
            return count;
        }
    }
}
