using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace YuudongDeepMining
{
    // 층 넘는 직접 운반: 이 층에 둘 곳이 없는 물건을, 갱도로 이어진 다른 층 창고까지 림이 들고 간다.
    // 물건을 들고 → 사다리를 타고(여러 층이면 반복) → 도착한 층 창고 칸에 내려놓는다.
    public static class HaulAcrossUtility
    {
        private struct CacheEntry
        {
            public Map target;
            public int tick;
        }

        // 같은 지도·같은 물건 종류의 '받아줄 층' 판단은 잠깐 기억해 둔다 (운반꾼마다 매번 모든 층 창고를 뒤지지 않도록).
        private static readonly Dictionary<(Map, ThingDef, StoragePriority), CacheEntry> cache = new Dictionary<(Map, ThingDef, StoragePriority), CacheEntry>();

        private const int CacheTicks = 250;

        // 이 물건을 받아줄 창고가 있는 다른 층.
        public static Map StorageLayerFor(Thing t, Faction faction, Map from, bool useCache)
        {
            return StorageLayerFor(t, faction, from, useCache, StoragePriority.Unstored);
        }

        // 이 물건을 지금 보관 우선순위(current)보다 높은 창고에 받아줄 다른 층.
        public static Map StorageLayerFor(Thing t, Faction faction, Map from, bool useCache, StoragePriority current)
        {
            if (from == null)
            {
                return null;
            }

            int now = Find.TickManager.TicksGame;
            (Map, ThingDef, StoragePriority) key = (from, t.def, current);
            if (useCache && cache.TryGetValue(key, out CacheEntry entry) && now - entry.tick < CacheTicks && (entry.target == null || !entry.target.Disposed))
            {
                return entry.target;
            }

            // 받아줄 창고가 있는 층 중, 물건 자리에서 그 창고 칸까지 실제로 가는 거리가 가장 짧은 층.
            Map found = null;
            float bestCost = float.MaxValue;
            foreach (Map other in LayerUtility.Chain(from))
            {
                if (other == from)
                {
                    continue;
                }

                if (!StoreUtility.TryFindBestBetterStoreCellFor(t, null, other, current, faction, out IntVec3 cell, false))
                {
                    if (DeepMiningMod.Settings.debugLog)
                    {
                        int accepting = other.haulDestinationManager.AllGroupsListInPriorityOrder.Count(g => g.Settings.AllowedToAccept(t));
                        string level = LayerUtility.LayerLabel(LayerUtility.DepthOf(other));
                        LayerDebug.LogOnce("storage" + t.def.defName + level, "storage for " + t.LabelShort + " on " + level + ": "
                            + (accepting == 0 ? "no stockpile allows it" : accepting + " stockpiles allow it but no free cell"));
                    }

                    continue;
                }

                float cost = LayerUtility.TravelCost(from, t.PositionHeld, other, cell);
                if (cost < bestCost)
                {
                    found = other;
                    bestCost = cost;
                }
            }

            if (cache.Count > 512)
            {
                cache.Clear();
            }

            cache[key] = new CacheEntry { target = found, tick = now };
            return found;
        }

        private static readonly Dictionary<(Map, ThingDef), int> noCellUntil = new Dictionary<(Map, ThingDef), int>();

        // target 층에서 이 물건을 실제로 둘 칸: 운반꾼과 물건을 그 층 사다리 도착 칸에 있다고 치고 바닐라 창고 칸 찾기를 돌린다.
        // (그 층 예약·금지·사다리에서 갈 수 있는지·다른 운반꾼이 맡아 둔 칸까지 바닐라 판정 그대로 본다.)
        // 같은 물건 종류가 못 찾으면 잠깐 기억해서, 같은 덩어리 수십 개를 매번 다시 찾지 않는다.
        public static IntVec3 FindCellOn(Thing t, Pawn pawn, Map target, StoragePriority current)
        {
            int now = Find.TickManager.TicksGame;
            (Map, ThingDef) key = (target, t.def);
            if (noCellUntil.TryGetValue(key, out int until) && now < until)
            {
                return IntVec3.Invalid;
            }

            IntVec3 arrival = LayerUtility.ArrivalCellOn(pawn.Map, target);
            if (!arrival.IsValid)
            {
                return IntVec3.Invalid;
            }

            IntVec3 found = GhostUtility.RunThings(new Thing[] { pawn, t }, target, _ => arrival,
                () => StoreUtility.TryFindBestBetterStoreCellFor(t, pawn, target, current, pawn.Faction, out IntVec3 c, false) ? c : IntVec3.Invalid, IntVec3.Invalid);
            if (!found.IsValid)
            {
                if (noCellUntil.Count > 256)
                {
                    noCellUntil.Clear();
                }

                noCellUntil[key] = now + 60;
            }

            return found;
        }

        private struct StoredEntry
        {
            public List<Thing> things;
            public int stampTicks;
            public long stampMs;
        }

        private static readonly Dictionary<Map, StoredEntry> storedCache = new Dictionary<Map, StoredEntry>();

        // 이 층 창고에 있지만, 다른 층에 우선순위가 더 높고 그 물건을 받는 창고가 있는 물건들.
        // 다른 층에 더 높은 창고가 하나도 없으면 바로 빈 목록이라 보통은 거의 비용이 없다.
        public static List<Thing> StoredCandidates(Map map)
        {
            if (!storedCache.TryGetValue(map, out StoredEntry e))
            {
                e = new StoredEntry { things = new List<Thing>() };
            }

            if (!CountAggregate.Stale(ref e.stampTicks, ref e.stampMs))
            {
                return e.things;
            }

            e.things = new List<Thing>();
            List<SlotGroup> others = new List<SlotGroup>();
            foreach (Map other in LayerUtility.Chain(map))
            {
                if (other != map)
                {
                    others.AddRange(other.haulDestinationManager.AllGroupsListInPriorityOrder);
                }
            }

            if (others.Count > 0)
            {
                foreach (SlotGroup group in map.haulDestinationManager.AllGroupsListInPriorityOrder)
                {
                    StoragePriority p = group.Settings.Priority;
                    List<SlotGroup> better = others.Where(o => o.Settings.Priority > p).ToList();
                    if (better.Count == 0)
                    {
                        continue;
                    }

                    foreach (Thing t in group.HeldThings)
                    {
                        if (better.Any(o => o.Settings.AllowedToAccept(t)))
                        {
                            e.things.Add(t);
                        }
                    }
                }
            }

            if (storedCache.Count > 32)
            {
                storedCache.Clear();
            }

            storedCache[map] = e;
            return e.things;
        }

        // 물건을 든 채 포털을 지난다 (바닐라 사다리 이동은 소집 안 한 림의 물건을 내려놓게 하므로 따로 만든다).
        public static bool TeleportCarrying(Pawn pawn, MapPortal portal)
        {
            IntVec3 cell = LayerUtility.ArrivalCell(portal, out Map other);
            if (other == null || !cell.IsValid)
            {
                return false;
            }

            pawn.DeSpawnOrDeselect();
            GenSpawn.Spawn(pawn, cell, other, Rot4.Random);
            portal.OnEntered(pawn);
            return true;
        }

        public static Job ContinueHaul(Pawn pawn, TravelOrder order, GameComponent_LayerTravel comp)
        {
            Thing carried = pawn.carryTracker?.CarriedThing;
            if (carried == null)
            {
                comp.ClearOrder(pawn);
                return null;
            }

            if (pawn.Map == order.target)
            {
                comp.ClearOrder(pawn);
                if (carried is Pawn takee)
                {
                    return CarryPawnsUtility.HandOffToVanilla(pawn, takee, order);
                }

                if (order.destination != null && order.destination.Spawned && order.destination.Map == pawn.Map)
                {
                    return BillSupplyUtility.DeliverJob(pawn, carried, order.destination);
                }

                IntVec3 cell = order.cell;
                if (!cell.IsValid || !cell.InBounds(pawn.Map) || !StoreUtility.IsGoodStoreCell(cell, pawn.Map, carried, pawn, pawn.Faction)
                    || !(StoreUtility.GetSlotGroup(cell, pawn.Map)?.Settings.AllowedToAccept(carried) ?? false))
                {
                    StoreClaims.Release(pawn);
                    if (!StoreUtility.TryFindBestBetterStoreCellFor(carried, pawn, pawn.Map, StoragePriority.Unstored, pawn.Faction, out cell))
                    {
                        return DropAwayFromPortal(pawn, carried);
                    }
                }

                Job place = JobMaker.MakeJob(YDMDefOf.YDM_PlaceCarried, cell);
                place.count = carried.stackCount;
                return place;
            }

            MapPortal portal = LayerUtility.NextPortalToward(pawn.Map, order.target);
            if (portal == null || !pawn.CanReach(portal, PathEndMode.Touch, Danger.Some))
            {
                comp.ClearOrder(pawn);
                return null;
            }

            return JobMaker.MakeJob(YDMDefOf.YDM_CarryThroughPortal, portal);
        }

        // 도착했는데 둘 창고가 없을 때: 사다리 길목을 막지 않게 조금 떨어진 빈 칸에 내려놓고,
        // 그 물건은 잠깐 다른 층 운반 대상에서 뺀다 (다시 실려 가는 왕복 방지). 자리가 나면 이 층 바닐라 운반이 옮긴다.
        private static Job DropAwayFromPortal(Pawn pawn, Thing carried)
        {
            Map map = pawn.Map;
            Thing portal = (Thing)LayerUtility.UpExit(map) ?? LayerUtility.DownShaft(map);
            IntVec3 root = pawn.Position;
            if (!CellFinder.TryFindRandomCellNear(root, map, 8, c => c.Standable(map) && c.GetFirstItem(map) == null && (portal == null || !c.InHorDistOf(portal.Position, 3f))
                && pawn.CanReach(c, PathEndMode.OnCell, Danger.Deadly), out IntVec3 cell))
            {
                cell = root;
            }

            if (LayerDebug.On)
            {
                LayerDebug.Log(pawn, "no storage for " + carried.LabelShort + " on arrival, dropped at " + cell);
            }

            BillSupplyUtility.MarkDelivered(carried);
            Job job = JobMaker.MakeJob(YDMDefOf.YDM_PlaceCarried, cell);
            job.count = carried.stackCount;
            job.haulMode = HaulMode.ToCellNonStorage;
            return job;
        }

        public static Toil WaitAtPortal(JobDriver driver, TargetIndex portalInd)
        {
            Toil wait = Toils_General.Wait(90).FailOnCannotTouch(portalInd, PathEndMode.Touch).WithProgressBarToilDelay(portalInd, true);
            wait.handlingFacing = true;
            wait.tickIntervalAction = (Action<int>)Delegate.Combine(wait.tickIntervalAction, (Action<int>)delegate
            {
                driver.pawn.rotationTracker.FaceTarget(driver.job.GetTarget(portalInd));
            });
            return wait;
        }

        // 포털을 지나고, 아직 목표 층이 아니면 '운반' 이동을 걸어 둔다 (다음 생각에서 이어 간다).
        public static Toil Teleport(JobDriver driver, TargetIndex portalInd)
        {
            Toil toil = ToilMaker.MakeToil("YDM_TeleportCarrying");
            toil.initAction = delegate
            {
                Pawn pawn = driver.pawn;
                Thing carried = pawn.carryTracker.CarriedThing;
                if (carried == null || !(driver.job.GetTarget(portalInd).Thing is MapPortal portal))
                {
                    return;
                }

                // 목표 층: 이어 가는 중이면 그 층, 사람을 옮기면 데려갈 침대가 있는 층, 물건이면 받아줄 창고가 있는 층.
                GameComponent_LayerTravel comp = GameComponent_LayerTravel.Instance;
                TravelOrder current = comp?.GetOrder(pawn);
                TravelOrder next = current?.kind == TravelKind.Haul ? current : new TravelOrder { kind = TravelKind.Haul };
                if (next.target == null)
                {
                    if (carried is Pawn && driver.job.targetC.HasThing)
                    {
                        next.bed = driver.job.targetC.Thing;
                        next.capture = driver.job.def == YDMDefOf.YDM_CaptureAcrossLayers;
                        next.target = next.bed.MapHeld;
                    }
                    else if (driver.job.targetC.HasThing)
                    {
                        // 다른 층 작업대로 재료를 가져가는 중.
                        next.destination = driver.job.targetC.Thing;
                        next.target = next.destination.MapHeld;
                    }
                    else if (StoreClaims.TryGet(pawn, out Map claimedMap, out IntVec3 claimedCell))
                    {
                        next.target = claimedMap;
                        next.cell = claimedCell;
                    }
                    else
                    {
                        next.target = StorageLayerFor(carried, pawn.Faction, pawn.Map, false);
                    }
                }

                if (next.target == null || next.target == pawn.Map)
                {
                    return;
                }

                if (TeleportCarrying(pawn, portal))
                {
                    comp?.SetOrder(pawn, next);
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }
    }

    public class WorkGiver_HaulAcrossLayers : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.ClosestTouch;

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            Map map = pawn.Map;
            return map == null || (!LayerUtility.IsLayer(map) && LayerUtility.MapBelow(map) == null)
                || (map.listerHaulables.ThingsPotentiallyNeedingHauling().Count == 0 && HaulAcrossUtility.StoredCandidates(map).Count == 0);
        }

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            ICollection<Thing> loose = pawn.Map.listerHaulables.ThingsPotentiallyNeedingHauling();
            List<Thing> stored = HaulAcrossUtility.StoredCandidates(pawn.Map);
            return stored.Count == 0 ? loose : loose.Concat(stored);
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            using YDMProfiler.Scope __perf = YDMProfiler.Measure(YDMProfiler.Cat.HaulScan);
            if (!HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, t, forced) || (!forced && BillSupplyUtility.RecentlyDelivered(t)))
            {
                return false;
            }

            // 이 층에 지금보다 좋은 창고가 있으면 바닐라 운반에 맡긴다.
            StoragePriority current = StoreUtility.CurrentStoragePriorityOf(t);
            if (StoreUtility.TryFindBestBetterStorageFor(t, pawn, pawn.Map, current, pawn.Faction, out IntVec3 _, out IHaulDestination _, false))
            {
                return false;
            }

            Map target = HaulAcrossUtility.StorageLayerFor(t, pawn.Faction, pawn.Map, true, current);
            MapPortal portal = LayerUtility.NextPortalToward(pawn.Map, target);
            bool ok = portal != null && pawn.CanReserve(t, 1, -1, null, forced) && pawn.CanReach(portal, PathEndMode.Touch, Danger.Some)
                && HaulAcrossUtility.FindCellOn(t, pawn, target, current).IsValid;
            if (LayerDebug.On)
            {
                LayerDebug.Log(pawn, "haul " + t.LabelShort + ": " + (target == null ? "no storage on other levels" : portal == null ? "no portal" : ok ? "ok -> " + LayerUtility.LayerLabel(LayerUtility.DepthOf(target)) : "can't reserve/reach"));
            }

            return ok;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            StoragePriority current = StoreUtility.CurrentStoragePriorityOf(t);
            Map target = HaulAcrossUtility.StorageLayerFor(t, pawn.Faction, pawn.Map, true, current);
            MapPortal portal = LayerUtility.NextPortalToward(pawn.Map, target);
            IntVec3 cell = portal == null ? IntVec3.Invalid : HaulAcrossUtility.FindCellOn(t, pawn, target, current);
            if (!cell.IsValid)
            {
                return null;
            }

            // 그 칸을 맡아 둔다: 다른 운반꾼(바닐라 포함)은 이 칸을 예약된 칸처럼 건너뛴다. (다른 층 판단 중에는 맡지 않는다.)
            if (!GhostUtility.Active)
            {
                StoreClaims.Claim(pawn, target, cell);
            }

            Job job = JobMaker.MakeJob(YDMDefOf.YDM_HaulAcrossLayers, t, portal);
            job.count = Mathf.Min(t.stackCount, pawn.carryTracker.AvailableStackSpace(t.def));
            return job;
        }
    }

    // 물건을 집어 들고 사다리로 가서 다른 층으로 넘어간다.
    public class JobDriver_HaulAcrossLayers : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.GetTarget(TargetIndex.A), job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.B);
            this.FailOnDestroyedNullOrForbidden(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch).FailOnSomeonePhysicallyInteracting(TargetIndex.A);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A, false, true);
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
            yield return HaulAcrossUtility.WaitAtPortal(this, TargetIndex.B);
            yield return HaulAcrossUtility.Teleport(this, TargetIndex.B);
        }
    }

    // 물건을 든 채 다음 사다리를 지난다 (여러 층을 넘을 때).
    public class JobDriver_CarryThroughPortal : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOn(() => pawn.carryTracker.CarriedThing == null);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return HaulAcrossUtility.WaitAtPortal(this, TargetIndex.A);
            yield return HaulAcrossUtility.Teleport(this, TargetIndex.A);
        }
    }

    // 들고 온 물건을 창고 칸에 내려놓는다.
    public class JobDriver_PlaceCarried : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.GetTarget(TargetIndex.A), job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => pawn.carryTracker.CarriedThing == null);
            AddFinishAction(delegate(JobCondition _)
            {
                StoreClaims.Release(pawn);
            });
            Toil carryToCell = Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.ClosestTouch);
            yield return carryToCell;
            yield return Toils_Haul.PlaceHauledThingInCell(TargetIndex.A, carryToCell, job.haulMode != HaulMode.ToCellNonStorage);
        }
    }

    // 다른 층으로 들고 가는 물건의 도착 창고 칸. 출발할 때 맡아 두고, 내려놓거나 운반이 끝나면 놓는다.
    // 바닐라 칸 예약은 그 지도에 서 있는 림만 할 수 있어서, 사다리를 넘는 동안의 몫을 따로 기억한다.
    public static class StoreClaims
    {
        private struct Entry
        {
            public Map map;
            public IntVec3 cell;
            public int tick;
        }

        private static readonly Dictionary<Pawn, Entry> byPawn = new Dictionary<Pawn, Entry>();
        private static readonly Dictionary<(Map, IntVec3), Pawn> byCell = new Dictionary<(Map, IntVec3), Pawn>();

        public static bool Any => byCell.Count > 0;

        public static void Claim(Pawn pawn, Map map, IntVec3 cell)
        {
            Release(pawn);
            byPawn[pawn] = new Entry { map = map, cell = cell, tick = Find.TickManager.TicksGame };
            byCell[(map, cell)] = pawn;
        }

        public static void Release(Pawn pawn)
        {
            if (pawn != null && byPawn.TryGetValue(pawn, out Entry c))
            {
                byPawn.Remove(pawn);
                byCell.Remove((c.map, c.cell));
            }
        }

        public static bool TryGet(Pawn pawn, out Map map, out IntVec3 cell)
        {
            map = null;
            cell = IntVec3.Invalid;
            if (pawn == null || !byPawn.TryGetValue(pawn, out Entry c) || !Valid(pawn, c))
            {
                return false;
            }

            map = c.map;
            cell = c.cell;
            return true;
        }

        // 다른 림이 맡아 둔 칸인가. 맡은 림이 더는 그 운반 중이 아니면 그 자리에서 놓는다.
        public static bool IsClaimedByOther(Map map, IntVec3 cell, Pawn carrier)
        {
            if (!byCell.TryGetValue((map, cell), out Pawn owner) || owner == carrier)
            {
                return false;
            }

            if (byPawn.TryGetValue(owner, out Entry c) && Valid(owner, c))
            {
                return true;
            }

            Release(owner);
            byCell.Remove((map, cell));
            return false;
        }

        // 맡은 림이 아직 그 운반(들기·사다리 넘기·내려놓기, 또는 운반 이동 명령) 중인가. 막 맡은 직후(작업 시작 전)도 인정한다.
        private static bool Valid(Pawn pawn, Entry c)
        {
            if (pawn.Dead || pawn.Destroyed || c.map == null || c.map.Disposed)
            {
                return false;
            }

            JobDef job = pawn.CurJobDef;
            if (job == YDMDefOf.YDM_HaulAcrossLayers || job == YDMDefOf.YDM_CarryThroughPortal || job == YDMDefOf.YDM_PlaceCarried)
            {
                return true;
            }

            if (GameComponent_LayerTravel.Instance?.GetOrder(pawn)?.kind == TravelKind.Haul)
            {
                return true;
            }

            return Find.TickManager.TicksGame - c.tick < 120;
        }
    }

    // 바닐라 창고 칸 판정: 다른 림이 다른 층에서 들고 오는 중인 칸은 예약된 칸처럼 본다.
    [HarmonyPatch(typeof(StoreUtility), nameof(StoreUtility.IsGoodStoreCell))]
    internal static class StoreUtility_IsGoodStoreCell_Claims_Patch
    {
        private static void Postfix(IntVec3 c, Map map, Pawn carrier, ref bool __result)
        {
            if (__result && StoreClaims.Any && StoreClaims.IsClaimedByOther(map, c, carrier))
            {
                __result = false;
            }
        }
    }
}
