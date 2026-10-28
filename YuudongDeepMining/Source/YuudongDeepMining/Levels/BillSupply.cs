using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
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

        private static Dictionary<ThingDef, HashSet<WorkTypeDef>> benchWorkTypes;

        // 이 작업 종류가 이 작업대의 이 지시서를 맡는가. 운반 작업은 모든 지시서를 맡는다 (운반꾼이 재료를 가져다 둔다).
        public static bool HandledBy(WorkTypeDef workType, ThingDef bench, Bill bill)
        {
            if (workType == null || workType == WorkTypeDefOf.Hauling)
            {
                return true;
            }

            if (bill.recipe.requiredGiverWorkType != null)
            {
                return bill.recipe.requiredGiverWorkType == workType;
            }

            if (benchWorkTypes == null)
            {
                benchWorkTypes = new Dictionary<ThingDef, HashSet<WorkTypeDef>>();
                foreach (WorkGiverDef wg in DefDatabase<WorkGiverDef>.AllDefsListForReading)
                {
                    if (wg.giverClass == null || !typeof(WorkGiver_DoBill).IsAssignableFrom(wg.giverClass) || wg.fixedBillGiverDefs == null || wg.workType == null)
                    {
                        continue;
                    }

                    foreach (ThingDef b in wg.fixedBillGiverDefs)
                    {
                        if (!benchWorkTypes.TryGetValue(b, out HashSet<WorkTypeDef> set))
                        {
                            benchWorkTypes[b] = set = new HashSet<WorkTypeDef>();
                        }

                        set.Add(wg.workType);
                    }
                }
            }

            return benchWorkTypes.TryGetValue(bench, out HashSet<WorkTypeDef> types) && types.Contains(workType);
        }

        // 다른 층에서 작업대·청사진 옆에 가져다 놓은 재료. 그 층에 받아줄 창고가 없으면 다른 층 운반이
        // 곧바로 원래 층 창고로 되가져가므로, 잠깐(작업자가 쓸 시간) 다른 층 운반 대상에서 뺀다.
        private static readonly Dictionary<Thing, int> delivered = new Dictionary<Thing, int>();

        private const int DeliveredTicks = 2500;

        public static void MarkDelivered(Thing thing)
        {
            int now = Find.TickManager.TicksGame;
            if (delivered.Count > 256)
            {
                delivered.RemoveAll(kv => now - kv.Value > DeliveredTicks || kv.Key.Destroyed);
            }

            delivered[thing] = now;
        }

        public static bool RecentlyDelivered(Thing thing)
        {
            return delivered.TryGetValue(thing, out int tick) && Find.TickManager.TicksGame - tick <= DeliveredTicks;
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
            MarkDelivered(carried);
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
            if (!CarryPawnsUtility.InChain(map) || comp == null || !comp.ScanDue(pawn, (def, GhostUtility.Active)))
            {
                return null;
            }

            List<ThingCount> chosen = new List<ThingCount>();
            foreach (Map other in LayerUtility.Chain(map))
            {
                MapPortal portal = other == map || !LayerUtility.CanTravel(map, other) ? null : LayerUtility.NextPortalToward(map, other);
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
                        if (bill.suspended || !bill.ShouldDoNow() || bill.recipe.ingredients.NullOrEmpty() || !BillSupplyUtility.HandledBy(def.workType, benchThing.def, bill))
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
                        if (LayerDebug.On)
                        {
                            LayerDebug.Log(pawn, "supply " + bill.LabelCap + " on " + LayerUtility.LayerLabel(LayerUtility.DepthOf(other)) + ": " + (pick.Thing == null ? "ingredients not here" : pick.Thing.LabelShort));
                        }

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

    // 운반 작업: 다른 층 청사진·건설 중인 틀에 모자란 건설 재료를 이 층에서 가져간다.
    // 바닐라 건설 재료 운반은 그 지도 안에서만 재료를 찾으므로, 그 층에 없는 몫만 들고 가서 청사진 옆에 내려놓는다.
    // 그다음(틀에 넣고 짓기)은 바닐라가 그 층에서 한다.
    public class WorkGiver_SupplyConstructionAcrossLayers : WorkGiver
    {
        public override Job NonScanJob(Pawn pawn)
        {
            using YDMProfiler.Scope __perf = YDMProfiler.Measure(YDMProfiler.Cat.BillSupply);
            Map map = pawn.Map;
            GameComponent_LayerTravel comp = GameComponent_LayerTravel.Instance;
            if (!CarryPawnsUtility.InChain(map) || comp == null || !comp.ScanDue(pawn, (def, GhostUtility.Active)))
            {
                return null;
            }

            foreach (Map other in LayerUtility.Chain(map))
            {
                MapPortal portal = other == map || !LayerUtility.CanTravel(map, other) ? null : LayerUtility.NextPortalToward(map, other);
                if (portal == null || !pawn.CanReach(portal, PathEndMode.Touch, Danger.Deadly))
                {
                    continue;
                }

                // 그 층 재고 중 아직 다른 청사진 몫으로 잡히지 않은 수.
                Dictionary<ThingDef, int> stock = new Dictionary<ThingDef, int>();
                foreach (Thing site in other.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).Concat(other.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame)))
                {
                    // 설치 청사진은 재료가 필요 없다 (바닐라는 재료 목록을 물으면 오류를 남긴다).
                    if (!(site is IConstructible c) || site is Blueprint_Install || site.Faction != Faction.OfPlayer || c.IsCompleted() || site.IsForbidden(Faction.OfPlayer))
                    {
                        continue;
                    }

                    foreach (ThingDefCountClass cost in c.TotalMaterialCost())
                    {
                        if (!stock.TryGetValue(cost.thingDef, out int have))
                        {
                            have = CountOn(other, cost.thingDef, pawn);
                        }

                        int need = Mathf.Max(0, c.ThingCountNeeded(cost.thingDef) - InTransit(site, cost.thingDef));
                        int missing = need - have;
                        stock[cost.thingDef] = Mathf.Max(0, have - need);
                        if (missing <= 0)
                        {
                            continue;
                        }

                        Thing pick = GenClosest.ClosestThingReachable(pawn.Position, map, ThingRequest.ForDef(cost.thingDef), PathEndMode.ClosestTouch, TraverseParms.For(pawn), 9999f,
                            t => !t.IsForbidden(pawn) && pawn.CanReserve(t));
                        if (LayerDebug.On)
                        {
                            LayerDebug.Log(pawn, "construction " + site.LabelShort + " on " + LayerUtility.LayerLabel(LayerUtility.DepthOf(other)) + " needs " + missing + " " + cost.thingDef.label + ": " + (pick == null ? "none here" : "from " + pick.LabelShort));
                        }

                        if (pick == null)
                        {
                            continue;
                        }

                        Job job = JobMaker.MakeJob(YDMDefOf.YDM_HaulAcrossLayers, pick, portal, site);
                        job.count = Mathf.Min(missing, pick.stackCount, pawn.carryTracker.AvailableStackSpace(pick.def));
                        job.workGiverDef = def;
                        return job.count > 0 ? job : null;
                    }
                }
            }

            return null;
        }

        // 그 층에 이미 있는(금지되지 않은) 재료 수. 묶음 합산을 거치지 않은 그 층만의 값.
        private static int CountOn(Map map, ThingDef def, Pawn pawn)
        {
            int n = 0;
            foreach (Thing t in map.listerThings.ThingsOfDef(def))
            {
                if (t.Spawned && !t.IsForbidden(Faction.OfPlayer))
                {
                    n += t.stackCount;
                }
            }

            return n;
        }

        // 이 청사진으로 가져가는 중인 재료 수 (아직 이 층에서 들고 가는 중이거나, 사다리를 넘는 중).
        private static int InTransit(Thing site, ThingDef def)
        {
            int n = 0;
            GameComponent_LayerTravel comp = GameComponent_LayerTravel.Instance;
            foreach (Map m in LayerUtility.Chain(site.Map))
            {
                foreach (Pawn p in m.mapPawns.FreeColonistsSpawned)
                {
                    Job j = p.CurJob;
                    if (j != null && j.def == YDMDefOf.YDM_HaulAcrossLayers && j.targetC.Thing == site && j.targetA.Thing?.def == def)
                    {
                        n += j.count;
                    }
                    else if (comp?.GetOrder(p) is TravelOrder o && o.destination == site && p.carryTracker.CarriedThing?.def == def)
                    {
                        n += p.carryTracker.CarriedThing.stackCount;
                    }
                }
            }

            return n;
        }
    }

    // 건설 메뉴: 재질을 고를 때 바닐라는 '이 지도에 그 재질이 하나라도 있는지'(listerThings.ThingsOfDef(재질).Count > 0)만 본다.
    // 건설 메뉴 함수 안의 그 호출 하나만 '이 지도에 없으면 같은 갱도 줄의 다른 층도 본다'로 바꾼다 (원본의 나머지는 그대로).
    // 자주 불리는 ListerThings 쪽에 패치를 걸지 않으므로 다른 곳 성능에는 영향이 없다.
    [HarmonyPatch(typeof(Designator_Build), nameof(Designator_Build.ProcessInput))]
    internal static class Designator_Build_ProcessInput_Stuff_Patch
    {
        private static readonly MethodInfo ThingsOfDef = AccessTools.Method(typeof(ListerThings), nameof(ListerThings.ThingsOfDef));

        private static readonly MethodInfo AcrossLevels = AccessTools.Method(typeof(Designator_Build_ProcessInput_Stuff_Patch), nameof(ThingsOfDefAcrossLevels));

        public static int Replaced;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction ci in instructions)
            {
                if ((ci.opcode == OpCodes.Callvirt || ci.opcode == OpCodes.Call) && ci.operand is MethodInfo m && m == ThingsOfDef)
                {
                    Replaced++;
                    yield return new CodeInstruction(OpCodes.Call, AcrossLevels).MoveLabelsFrom(ci).MoveBlocksFrom(ci);
                    continue;
                }

                yield return ci;
            }
        }

        public static List<Thing> ThingsOfDefAcrossLevels(ListerThings lister, ThingDef def)
        {
            List<Thing> here = lister.ThingsOfDef(def);
            if (here.Count > 0 || GhostUtility.Active)
            {
                return here;
            }

            Map current = Find.CurrentMap;
            List<Map> chain = CountAggregate.ChainOf(current);
            if (chain == null)
            {
                return here;
            }

            foreach (Map other in chain)
            {
                if (other != current)
                {
                    List<Thing> theirs = other.listerThings.ThingsOfDef(def);
                    if (theirs.Count > 0)
                    {
                        return theirs;
                    }
                }
            }

            return here;
        }
    }
}
