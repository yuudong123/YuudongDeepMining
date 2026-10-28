using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace YuudongDeepMining
{
    // 사람 옮기기: 쓰러진 림을 다른 층 침대로 구조하거나, 다른 층 감옥으로 포획한다.
    // 판단은 바닐라 구조 작업(DoctorRescue)과 침대 찾기를 다른 층에서 고스트로 돌려 얻고(옮기는 림과 쓰러진 림 둘 다 그 층 사다리 앞에 있다고 친다),
    // 실행은 '들고 사다리 넘기' 뒤 내려놓고 바닐라 구조·포획 작업을 그대로 시킨다 (침대에 눕히는 것까지 바닐라 코드).
    public static class CarryPawnsUtility
    {
        private static WorkGiver_RescueDowned vanillaRescue;

        public static WorkGiver_RescueDowned VanillaRescue => vanillaRescue ??= DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorRescue")?.Worker as WorkGiver_RescueDowned;

        public static bool InChain(Map map)
        {
            return map != null && (LayerUtility.IsLayer(map) || LayerUtility.MapBelow(map) != null);
        }

        // 다른 층에서 takee를 눕힐 침대를 찾는다. requireRescueJob이면 바닐라 구조 작업 판단까지 통과해야 한다.
        public static bool FindBedAcross(Pawn carrier, Pawn takee, GuestStatus? guest, bool requireRescueJob, bool forced, out Map target, out Building_Bed bed)
        {
            target = null;
            bed = null;
            Map map = takee.Map;
            float bestCost = float.MaxValue;
            Pawn[] both = { carrier, takee };
            foreach (Map other in LayerUtility.Chain(map))
            {
                IntVec3 cell = other == map ? IntVec3.Invalid : LayerUtility.ArrivalCellOn(map, other);
                MapPortal portal = LayerUtility.CanTravel(map, other) ? LayerUtility.NextPortalToward(map, other) : null;
                if (!cell.IsValid || portal == null || !map.reachability.CanReach(takee.Position, portal, PathEndMode.Touch, TraverseParms.For(carrier, Danger.Deadly)))
                {
                    continue;
                }

                Building_Bed found = GhostUtility.Run(both, other, _ => cell, delegate
                {
                    if (requireRescueJob && (VanillaRescue == null || !VanillaRescue.HasJobOnThing(carrier, takee, forced)))
                    {
                        return null;
                    }

                    return RestUtility.FindBedFor(takee, carrier, false, false, guest) ?? RestUtility.FindBedFor(takee, carrier, false, true, guest);
                }, null);
                if (found == null)
                {
                    continue;
                }

                float cost = LayerUtility.TravelCost(map, takee.Position, other, found.Position);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    target = other;
                    bed = found;
                }
            }

            return bed != null;
        }

        public static Job MakeJob(Pawn takee, Building_Bed bed, bool capture)
        {
            MapPortal portal = LayerUtility.NextPortalToward(takee.Map, bed.MapHeld);
            if (portal == null)
            {
                return null;
            }

            Job job = JobMaker.MakeJob(capture ? YDMDefOf.YDM_CaptureAcrossLayers : YDMDefOf.YDM_RescueAcrossLayers, takee, portal, bed);
            job.count = 1;
            return job;
        }

        // 목표 층에 왔다: 내려놓고 바닐라 구조(또는 포획) 작업으로 침대에 눕힌다.
        public static Job HandOffToVanilla(Pawn pawn, Pawn takee, TravelOrder order)
        {
            pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out Thing _);
            GuestStatus? guest = order.capture ? GuestStatus.Prisoner : null;
            Building_Bed bed = order.bed as Building_Bed;
            if (bed == null || !bed.Spawned || bed.Map != pawn.Map)
            {
                bed = RestUtility.FindBedFor(takee, pawn, false, false, guest) ?? RestUtility.FindBedFor(takee, pawn, false, true, guest);
            }

            if (bed == null || !takee.Spawned)
            {
                return null;
            }

            Job job = JobMaker.MakeJob(order.capture ? JobDefOf.Capture : JobDefOf.Rescue, takee, bed);
            job.count = 1;
            return job;
        }

        // 바닐라 구조 메뉴와 같은 조건 (구조 대상인가).
        public static bool RescueEligible(Pawn takee)
        {
            if (takee.mindState.WillJoinColonyIfRescued || takee.IsPrisonerOfColony || takee.IsSlaveOfColony || takee.IsColonyMech)
            {
                return false;
            }

            if (takee.Faction != null && takee.Faction.HostileTo(Faction.OfPlayer))
            {
                return false;
            }

            if (ChildcareUtility.CanSuckle(takee, out ChildcareUtility.BreastfeedFailReason? _))
            {
                return false;
            }

            if (!HealthAIUtility.ShouldSeekMedicalRest(takee) && takee.ageTracker.CurLifeStage.alwaysDowned)
            {
                return false;
            }

            return takee.playerSettings == null || takee.playerSettings.medCare != MedicalCareCategory.NoCare;
        }
    }

    // 의사 작업: 이 층에 눕힐 침대가 없는 쓰러진 아군을 다른 층 침대로 구조한다.
    public class WorkGiver_RescueAcrossLayers : WorkGiver_Scanner
    {
        private struct Plan
        {
            public Building_Bed bed;
            public int tick;
        }

        // 쓰러진 림마다 다른 층 침대 판단을 잠깐 기억한다 (의사마다 매번 모든 층을 고스트로 보지 않게).
        private static readonly Dictionary<Pawn, Plan> plans = new Dictionary<Pawn, Plan>();

        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Pawn);

        public override PathEndMode PathEndMode => PathEndMode.OnCell;

        public override Danger MaxPathDanger(Pawn pawn)
        {
            return Danger.Deadly;
        }

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            return pawn.Map.mapPawns.SpawnedDownedPawns;
        }

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            return !CarryPawnsUtility.InChain(pawn.Map) || CarryPawnsUtility.VanillaRescue == null || CarryPawnsUtility.VanillaRescue.ShouldSkip(pawn, forced);
        }

        private static Building_Bed PlanFor(Pawn pawn, Pawn takee, bool forced)
        {
            int now = Find.TickManager.TicksGame;
            if (plans.TryGetValue(takee, out Plan plan) && now - plan.tick < 250 && (plan.bed == null || plan.bed.Spawned))
            {
                return plan.bed;
            }

            CarryPawnsUtility.FindBedAcross(pawn, takee, null, true, forced, out Map _, out Building_Bed bed);
            if (plans.Count > 256)
            {
                plans.Clear();
            }

            plans[takee] = new Plan { bed = bed, tick = now };
            return bed;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            using YDMProfiler.Scope __perf = YDMProfiler.Measure(YDMProfiler.Cat.RescueScan);
            if (!(t is Pawn takee) || takee == pawn || !HealthAIUtility.CanRescueNow(pawn, takee, forced))
            {
                return false;
            }

            // 이 층에서 바닐라 구조가 되면 바닐라에 맡긴다.
            if (CarryPawnsUtility.VanillaRescue.HasJobOnThing(pawn, takee, forced))
            {
                return false;
            }

            Building_Bed bed = PlanFor(pawn, takee, forced);
            if (LayerDebug.On)
            {
                LayerDebug.Log(pawn, "rescue " + takee.LabelShort + ": " + (bed == null ? "no bed on other levels" : "bed on " + LayerUtility.LayerLabel(LayerUtility.DepthOf(bed.Map))));
            }

            return bed != null;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Building_Bed bed = t is Pawn takee ? PlanFor(pawn, takee, forced) : null;
            return bed == null ? null : CarryPawnsUtility.MakeJob((Pawn)t, bed, false);
        }
    }

    // 우클릭: 이 층에 침대·감옥 침대가 없을 때 '다른 층 침대로 구조', '다른 층 감옥으로 포획'.
    public class FloatMenuOptionProvider_CarryAcrossLayers : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;

        protected override bool Undrafted => true;

        protected override bool Multiselect => false;

        protected override bool RequiresManipulation => true;

        protected override FloatMenuOption GetSingleOptionFor(Pawn clickedPawn, FloatMenuContext context)
        {
            Pawn me = context.FirstSelectedPawn;
            Map map = clickedPawn.Map;
            if (GhostUtility.Active || me == null || me.Map != map || !CarryPawnsUtility.InChain(map))
            {
                return null;
            }

            bool capture = clickedPawn.CanBeCaptured();
            if (!capture && !CarryPawnsUtility.RescueEligible(clickedPawn))
            {
                return null;
            }

            if (!HealthAIUtility.CanRescueNow(me, clickedPawn, true))
            {
                return null;
            }

            GuestStatus? guest = capture ? GuestStatus.Prisoner : null;
            if (RestUtility.FindBedFor(clickedPawn, me, false, true, guest) != null)
            {
                return null;
            }

            if (!CarryPawnsUtility.FindBedAcross(me, clickedPawn, guest, false, true, out Map target, out Building_Bed bed))
            {
                return null;
            }

            string level = LayerUtility.LayerLabel(LayerUtility.DepthOf(target));
            string label = (capture ? "YDM.CaptureToLevel" : "YDM.RescueToLevel").Translate(clickedPawn.LabelShortCap, level);
            FloatMenuOption option = new FloatMenuOption(label, delegate
            {
                Job job = CarryPawnsUtility.MakeJob(clickedPawn, bed, capture);
                if (job != null)
                {
                    me.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                }
            }, MenuOptionPriority.RescueOrCapture, null, clickedPawn);
            return FloatMenuUtility.DecoratePrioritizedTask(option, me, clickedPawn);
        }
    }
}
