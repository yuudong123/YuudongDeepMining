using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace YuudongDeepMining
{
    // 고스트 판정: 바닐라의 작업 탐색·우클릭 메뉴·침대 찾기·개수 세기는 그 물건(림·작업대)이 그 지도에 있어야 계산된다.
    // 계산하는 동안만 물건의 지도·위치 값을 '다른 층 사다리 앞'으로 바꿨다가 곧바로 되돌린다.
    //  - 지도 격자·목록에는 손대지 않는다. 바뀐 상태로 게임이 진행되지 않도록 같은 호출 안에서만 쓴다.
    //  - 판정이 남기는 흔적(게임 난수 소모, 작업 지시서의 '재료 찾기 실패' 시각)은 되돌린다.
    //  - 예외가 나면 그 판정만 버리고, 바꾼 값은 finally에서 반드시 되돌린다.
    //  - 판정 안에서 또 다른 층을 판정할 수 있다 (최대 MaxDepth 겹).
    public static class GhostUtility
    {
        private const int MaxDepth = 2;

        private static readonly AccessTools.FieldRef<Thing, sbyte> MapIndexRef = AccessTools.FieldRefAccess<Thing, sbyte>("mapIndexOrState");

        private static readonly AccessTools.FieldRef<Thing, IntVec3> PositionRef = AccessTools.FieldRefAccess<Thing, IntVec3>("positionInt");

        private struct Saved
        {
            public Thing thing;
            public sbyte mapIndex;
            public IntVec3 position;
        }

        private class Frame
        {
            public readonly List<Saved> things = new List<Saved>();
            public readonly List<KeyValuePair<Bill, int>> bills = new List<KeyValuePair<Bill, int>>();
        }

        private static readonly List<Frame> frames = new List<Frame>();

        public static bool Active => frames.Count > 0;

        public static T Run<T>(IList<Pawn> pawns, Map map, Func<Pawn, IntVec3> cellFor, Func<T> func, T fallback)
        {
            List<Thing> things = new List<Thing>(pawns.Count);
            foreach (Pawn pawn in pawns)
            {
                things.Add(pawn);
            }

            return RunThings(things, map, t => cellFor((Pawn)t), func, fallback);
        }

        public static T Run<T>(Pawn pawn, Map map, IntVec3 cell, Func<T> func, T fallback)
        {
            return RunThings(new Thing[] { pawn }, map, _ => cell, func, fallback);
        }

        public static T RunThings<T>(IList<Thing> things, Map map, Func<Thing, IntVec3> cellFor, Func<T> func, T fallback)
        {
            int index = Find.Maps.IndexOf(map);
            if (index < 0 || frames.Count >= MaxDepth)
            {
                return fallback;
            }

            Frame frame = new Frame();
            Rand.PushState();
            SaveBills(map, frame);
            frames.Add(frame);
            try
            {
                foreach (Thing thing in things)
                {
                    IntVec3 cell = thing != null && thing.Spawned ? cellFor(thing) : IntVec3.Invalid;
                    if (!cell.IsValid || !cell.InBounds(map))
                    {
                        continue;
                    }

                    frame.things.Add(new Saved { thing = thing, mapIndex = MapIndexRef(thing), position = PositionRef(thing) });
                    MapIndexRef(thing) = (sbyte)index;
                    PositionRef(thing) = cell;
                }

                return frame.things.Count > 0 ? func() : fallback;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[YuudongDeepMining] 다른 층 판정 중 오류: " + ex, 0x59444D01);
                if (DeepMiningMod.Settings.debugLog)
                {
                    Log.Warning("[YDM] 다른 층 판정 오류 (" + (things.Count > 0 ? things[0]?.LabelShort : "") + "): " + ex);
                }

                return fallback;
            }
            finally
            {
                for (int i = frame.things.Count - 1; i >= 0; i--)
                {
                    Saved s = frame.things[i];
                    MapIndexRef(s.thing) = s.mapIndex;
                    PositionRef(s.thing) = s.position;
                }

                foreach (KeyValuePair<Bill, int> kv in frame.bills)
                {
                    kv.Key.nextTickToSearchForIngredients = kv.Value;
                }

                frames.RemoveAt(frames.Count - 1);
                Rand.PopState();
            }
        }

        private static void SaveBills(Map map, Frame frame)
        {
            foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.PotentialBillGiver))
            {
                if (thing is IBillGiver giver && giver.BillStack != null)
                {
                    foreach (Bill bill in giver.BillStack)
                    {
                        frame.bills.Add(new KeyValuePair<Bill, int>(bill, bill.nextTickToSearchForIngredients));
                    }
                }
            }
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
