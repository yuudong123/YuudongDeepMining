using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace YuudongDeepMining
{
    // 고스트 판정: 바닐라의 작업 탐색·우클릭 메뉴는 림이 그 지도에 있어야 계산된다.
    // 계산하는 동안만 림의 지도·위치 값을 '다른 층 사다리 앞'으로 바꿨다가 곧바로 되돌린다.
    // 지도 격자·목록에는 손대지 않으므로 다른 것은 바뀌지 않는다. 바뀐 상태로 게임이 진행되지 않도록 같은 호출 안에서만 쓴다.
    public static class GhostUtility
    {
        private static readonly AccessTools.FieldRef<Thing, sbyte> MapIndexRef = AccessTools.FieldRefAccess<Thing, sbyte>("mapIndexOrState");

        private static readonly AccessTools.FieldRef<Thing, IntVec3> PositionRef = AccessTools.FieldRefAccess<Thing, IntVec3>("positionInt");

        private struct Saved
        {
            public Pawn pawn;
            public sbyte mapIndex;
            public IntVec3 position;
        }

        private static readonly List<Saved> saved = new List<Saved>();

        public static bool Active => saved.Count > 0;

        public static T Run<T>(IList<Pawn> pawns, Map map, Func<Pawn, IntVec3> cellFor, Func<T> func, T fallback)
        {
            int index = Find.Maps.IndexOf(map);
            if (index < 0 || saved.Count > 0)
            {
                return fallback;
            }

            try
            {
                foreach (Pawn pawn in pawns)
                {
                    IntVec3 cell = pawn != null && pawn.Spawned ? cellFor(pawn) : IntVec3.Invalid;
                    if (!cell.IsValid || !cell.InBounds(map))
                    {
                        continue;
                    }

                    saved.Add(new Saved { pawn = pawn, mapIndex = MapIndexRef(pawn), position = PositionRef(pawn) });
                    MapIndexRef(pawn) = (sbyte)index;
                    PositionRef(pawn) = cell;
                }

                return saved.Count > 0 ? func() : fallback;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[YuudongDeepMining] 다른 층 판정 중 오류: " + ex, 0x59444D01);
                return fallback;
            }
            finally
            {
                foreach (Saved s in saved)
                {
                    MapIndexRef(s.pawn) = s.mapIndex;
                    PositionRef(s.pawn) = s.position;
                }

                saved.Clear();
            }
        }

        public static T Run<T>(Pawn pawn, Map map, IntVec3 cell, Func<T> func, T fallback)
        {
            return Run(new[] { pawn }, map, _ => cell, func, fallback);
        }

        // 바닐라 우클릭 메뉴는 '보고 있는 지도' 기준이다. 계산하는 동안만 현재 지도 번호를 바꾼다 (지도 전환 알림·선택 해제 없이).
        public static void WithCurrentMap(Map map, Action action)
        {
            Game game = Current.Game;
            int index = Find.Maps.IndexOf(map);
            if (game == null || index < 0)
            {
                return;
            }

            sbyte old = game.currentMapIndex;
            game.currentMapIndex = (sbyte)index;
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[YuudongDeepMining] 다른 층 명령 실행 중 오류: " + ex, 0x59444D02);
            }
            finally
            {
                game.currentMapIndex = old;
            }
        }
    }
}
