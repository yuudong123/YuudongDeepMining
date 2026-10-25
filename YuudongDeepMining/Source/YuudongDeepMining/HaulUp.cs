using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace YuudongDeepMining
{
    // 지하 층에서 보관할 곳이 없는 물건(출구 '저장소' 탭에서 고른 종류)을 위로 가는 계단에 올려 보낸다.
    // 물건은 위층 갱도 옆에 놓이고, 그다음은 위층 운반꾼이 평소처럼 창고로 옮긴다.
    public class WorkGiver_HaulUpShaft : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.ClosestTouch;

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            Building_MineShaftExit exit = LayerUtility.UpExit(pawn.Map);
            return exit == null || !exit.autoHaulUp || pawn.Map.listerHaulables.ThingsPotentiallyNeedingHauling().Count == 0;
        }

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            return pawn.Map.listerHaulables.ThingsPotentiallyNeedingHauling();
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Building_MineShaftExit exit = LayerUtility.UpExit(pawn.Map);
            if (exit == null || !exit.WantsHaulUp(t) || t.IsInValidStorage() || t.IsForbidden(pawn))
            {
                return false;
            }

            if (!HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, t, forced) || !pawn.CanReserve(t, 1, -1, null, forced))
            {
                return false;
            }

            // 이 층에 둘 곳이 있으면 바닐라 운반에 맡긴다.
            if (StoreUtility.TryFindBestBetterStorageFor(t, pawn, pawn.Map, StoreUtility.CurrentStoragePriorityOf(t), pawn.Faction, out IntVec3 _, out IHaulDestination _, false))
            {
                return false;
            }

            return pawn.CanReach(exit, PathEndMode.Touch, Danger.Some);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Building_MineShaftExit exit = LayerUtility.UpExit(pawn.Map);
            if (exit == null)
            {
                return null;
            }

            Job job = JobMaker.MakeJob(YDMDefOf.YDM_HaulUpShaft, t, exit);
            job.count = Mathf.Min(t.stackCount, pawn.carryTracker.AvailableStackSpace(t.def));
            job.haulMode = HaulMode.ToCellNonStorage;
            return job;
        }
    }

    public class JobDriver_HaulUpShaft : JobDriver
    {
        private const TargetIndex ThingInd = TargetIndex.A;

        private const TargetIndex ExitInd = TargetIndex.B;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.GetTarget(ThingInd), job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(ExitInd);
            this.FailOnDestroyedNullOrForbidden(ThingInd);
            yield return Toils_Goto.GotoThing(ThingInd, PathEndMode.ClosestTouch).FailOnSomeonePhysicallyInteracting(ThingInd);
            yield return Toils_Haul.StartCarryThing(ThingInd, false, true);
            yield return Toils_Goto.GotoThing(ExitInd, PathEndMode.Touch);
            Toil send = ToilMaker.MakeToil("SendUp");
            send.initAction = delegate
            {
                Thing carried = pawn.carryTracker.CarriedThing;
                if (carried == null || !(job.GetTarget(ExitInd).Thing is Building_MineShaftExit exit) || exit.entrance == null || !exit.entrance.Spawned)
                {
                    return;
                }

                Map upper = exit.entrance.Map;
                IntVec3 dest = exit.entrance.Position;
                pawn.carryTracker.innerContainer.Remove(carried);
                if (!GenPlace.TryPlaceThing(carried, dest, upper, ThingPlaceMode.Near))
                {
                    GenPlace.TryPlaceThing(carried, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                }
            };
            send.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return send;
        }
    }
}
