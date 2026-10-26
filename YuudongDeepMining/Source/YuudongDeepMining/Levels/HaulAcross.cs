using System;
using System.Collections.Generic;
using System.Linq;
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
        private static readonly Dictionary<(Map, ThingDef), CacheEntry> cache = new Dictionary<(Map, ThingDef), CacheEntry>();

        private const int CacheTicks = 250;

        // 이 물건을 받아줄 창고가 있는 다른 층.
        public static Map StorageLayerFor(Thing t, Faction faction, Map from, bool useCache)
        {
            if (from == null)
            {
                return null;
            }

            int now = Find.TickManager.TicksGame;
            (Map, ThingDef) key = (from, t.def);
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

                if (!StoreUtility.TryFindBestBetterStoreCellFor(t, null, other, StoragePriority.Unstored, faction, out IntVec3 cell, false))
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

                if (!StoreUtility.TryFindBestBetterStoreCellFor(carried, pawn, pawn.Map, StoragePriority.Unstored, pawn.Faction, out IntVec3 cell))
                {
                    return null;
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
            return map == null || (!LayerUtility.IsLayer(map) && LayerUtility.MapBelow(map) == null) || map.listerHaulables.ThingsPotentiallyNeedingHauling().Count == 0;
        }

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            return pawn.Map.listerHaulables.ThingsPotentiallyNeedingHauling();
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            using YDMProfiler.Scope __perf = YDMProfiler.Measure(YDMProfiler.Cat.HaulScan);
            if (t.IsInValidStorage() || !HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, t, forced))
            {
                return false;
            }

            // 이 층에 둘 곳이 있으면 바닐라 운반에 맡긴다.
            if (StoreUtility.TryFindBestBetterStorageFor(t, pawn, pawn.Map, StoragePriority.Unstored, pawn.Faction, out IntVec3 _, out IHaulDestination _, false))
            {
                return false;
            }

            Map target = HaulAcrossUtility.StorageLayerFor(t, pawn.Faction, pawn.Map, true);
            MapPortal portal = LayerUtility.NextPortalToward(pawn.Map, target);
            bool ok = portal != null && pawn.CanReserve(t, 1, -1, null, forced) && pawn.CanReach(portal, PathEndMode.Touch, Danger.Some);
            LayerDebug.Log(pawn, "haul " + t.LabelShort + ": " + (target == null ? "no storage on other levels" : portal == null ? "no portal" : ok ? "ok -> " + LayerUtility.LayerLabel(LayerUtility.DepthOf(target)) : "can't reserve/reach"));
            return ok;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Map target = HaulAcrossUtility.StorageLayerFor(t, pawn.Faction, pawn.Map, true);
            MapPortal portal = LayerUtility.NextPortalToward(pawn.Map, target);
            if (portal == null)
            {
                return null;
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
            Toil carryToCell = Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.ClosestTouch);
            yield return carryToCell;
            yield return Toils_Haul.PlaceHauledThingInCell(TargetIndex.A, carryToCell, job.haulMode != HaulMode.ToCellNonStorage);
        }
    }
}
