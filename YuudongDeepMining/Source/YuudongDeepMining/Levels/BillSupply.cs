using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace YuudongDeepMining
{
    // 다른 층 창고의 재료로 제작: 작업대가 있는 층에 재료가 없고, 이 층에 재료가 다 있으면 운반꾼이 들고 가서 작업대 옆에 내려놓는다.
    // 재료 판단은 바닐라 재료 찾기(WorkGiver_DoBill.TryFindBestBillIngredients)를 그대로 쓴다:
    //  - 작업대 층에 재료가 있는지: 운반꾼을 작업대 앞에 있다고 치고(고스트) 찾아본다.
    //  - 이 층에서 모을 수 있는지: 작업대를 이 층 사다리 앞에 있다고 치고(고스트) 찾아본다.
    // 한계: 재료가 여러 층에 나뉘어 있으면, 한 층에서 다 모을 수 있을 때만 옮긴다.
    public static class BillSupplyUtility
    {
        private static readonly MethodInfo TryFindIngredients = AccessTools.Method(typeof(WorkGiver_DoBill), "TryFindBestBillIngredients");

        private struct Verdict
        {
            public bool needsSupply;
            public List<IngredientCount> missing;
            public int tick;
        }

        // 작업 지시서마다 '작업대 층에 재료가 없다'는 판단을 잠깐 기억한다.
        private static readonly Dictionary<Bill, Verdict> verdicts = new Dictionary<Bill, Verdict>();

        private const int VerdictTicks = 600;

        public static bool FindIngredients(Bill bill, Pawn pawn, Thing benchThing, List<ThingCount> chosen, List<IngredientCount> missing)
        {
            if (TryFindIngredients == null)
            {
                return false;
            }

            chosen.Clear();
            missing.Clear();
            return (bool)TryFindIngredients.Invoke(null, new object[] { bill, pawn, benchThing, chosen, missing });
        }

        // 작업대 층 안에서는 재료를 못 모으는가 (운반꾼을 작업대 앞에 있다고 치고 바닐라로 찾아본다).
        // 못 모으면 빠진 재료 목록을 돌려준다 (이미 가져다 놓은 재료를 또 가져가지 않게).
        public static List<IngredientCount> MissingOnBenchLevel(Bill bill, Pawn pawn, Thing benchThing)
        {
            int now = Find.TickManager.TicksGame;
            if (verdicts.TryGetValue(bill, out Verdict v) && now - v.tick < VerdictTicks)
            {
                return v.needsSupply ? v.missing : null;
            }

            IntVec3 cell = benchThing.def.hasInteractionCell ? benchThing.InteractionCell : benchThing.Position;
            List<ThingCount> chosen = new List<ThingCount>();
            List<IngredientCount> missing = new List<IngredientCount>();
            bool there = GhostUtility.Run(pawn, benchThing.Map, cell, () => FindIngredients(bill, pawn, benchThing, chosen, missing), true);
            if (verdicts.Count > 512)
            {
                verdicts.Clear();
            }

            // 바닐라가 빠진 재료를 알려 주지 않으면 지시서의 재료 전체를 빠진 것으로 본다.
            List<IngredientCount> result = there ? null : (missing.Count > 0 ? new List<IngredientCount>(missing) : bill.recipe.ingredients);
            verdicts[bill] = new Verdict { needsSupply = !there, missing = result, tick = now };
            return result;
        }

        public static void Forget(Thing benchThing)
        {
            if (benchThing is IBillGiver giver && giver.BillStack != null)
            {
                foreach (Bill bill in giver.BillStack)
                {
                    verdicts.Remove(bill);
                    bill.nextTickToSearchForIngredients = 0;
                }
            }
        }

        // 작업대 옆 빈 칸에 내려놓는 작업.
        public static Job DeliverJob(Pawn pawn, Thing carried, Thing benchThing)
        {
            Map map = pawn.Map;
            IntVec3 root = benchThing.def.hasInteractionCell ? benchThing.InteractionCell : benchThing.Position;
            if (!CellFinder.TryFindRandomCellNear(root, map, 3, c => c.Standable(map) && c.GetFirstItem(map) == null && c != root && pawn.CanReach(c, PathEndMode.OnCell, Danger.Deadly), out IntVec3 cell))
            {
                cell = root;
            }

            Forget(benchThing);
            Job job = JobMaker.MakeJob(YDMDefOf.YDM_PlaceCarried, cell);
            job.count = carried.stackCount;
            job.haulMode = HaulMode.ToCellNonStorage;
            return job;
        }
    }

    // 운반 작업: 다른 층 작업대에 필요한 재료를 이 층에서 모아 가져간다.
    public class WorkGiver_SupplyBillsAcrossLayers : WorkGiver
    {
        public override Job NonScanJob(Pawn pawn)
        {
            using YDMProfiler.Scope __perf = YDMProfiler.Measure(YDMProfiler.Cat.BillSupply);
            Map map = pawn.Map;
            GameComponent_LayerTravel comp = GameComponent_LayerTravel.Instance;
            if (!CarryPawnsUtility.InChain(map) || comp == null || !comp.ScanDue(pawn, typeof(WorkGiver_SupplyBillsAcrossLayers)))
            {
                return null;
            }

            List<ThingCount> chosen = new List<ThingCount>();
            foreach (Map other in LayerUtility.Chain(map))
            {
                MapPortal portal = other == map ? null : LayerUtility.NextPortalToward(map, other);
                if (portal == null || !pawn.CanReach(portal, PathEndMode.Touch, Danger.Deadly))
                {
                    continue;
                }

                // 이 층에서 재료를 모아 그 층으로 가져갈 때의 출발점: 이 층 사다리(갱도) 앞.
                IntVec3 here = portal.Position.Standable(map) ? portal.Position : CellFinder.StandableCellNear(portal.Position, map, 3f);
                if (!here.IsValid)
                {
                    continue;
                }

                foreach (Thing benchThing in other.listerThings.ThingsInGroup(ThingRequestGroup.PotentialBillGiver))
                {
                    if (!(benchThing is IBillGiver giver) || benchThing is Pawn || giver.BillStack == null || !giver.BillStack.AnyShouldDoNow)
                    {
                        continue;
                    }

                    foreach (Bill bill in giver.BillStack)
                    {
                        if (bill.suspended || !bill.ShouldDoNow() || bill.recipe.ingredients.NullOrEmpty())
                        {
                            continue;
                        }

                        List<IngredientCount> missing = BillSupplyUtility.MissingOnBenchLevel(bill, pawn, benchThing);
                        if (missing.NullOrEmpty())
                        {
                            continue;
                        }

                        bool found = GhostUtility.RunThings(new[] { benchThing }, map, _ => here, () => BillSupplyUtility.FindIngredients(bill, pawn, benchThing, chosen, new List<IngredientCount>()), false);
                        ThingCount pick = found ? chosen.FirstOrDefault(tc => tc.Thing != null && tc.Thing.Spawned && tc.Thing.Map == map && missing.Any(m => m.filter.Allows(tc.Thing))
                            && !tc.Thing.IsForbidden(pawn) && pawn.CanReserve(tc.Thing)) : default;
                        LayerDebug.Log(pawn, "supply " + bill.LabelCap + " on " + LayerUtility.LayerLabel(LayerUtility.DepthOf(other)) + ": " + (pick.Thing == null ? "ingredients not here" : pick.Thing.LabelShort));
                        if (pick.Thing == null)
                        {
                            continue;
                        }

                        Job job = JobMaker.MakeJob(YDMDefOf.YDM_HaulAcrossLayers, pick.Thing, portal, benchThing);
                        job.count = Mathf.Min(pick.Count, pawn.carryTracker.AvailableStackSpace(pick.Thing.def));
                        job.workGiverDef = def;
                        return job.count > 0 ? job : null;
                    }
                }
            }

            return null;
        }
    }
}
