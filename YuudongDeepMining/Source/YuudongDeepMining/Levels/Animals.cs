using RimWorld;
using Verse;
using Verse.AI;

namespace YuudongDeepMining
{
    // 동물: 주인이 다른 층에 있으면 바닐라 '주인 따라가기' 조건(소집 따라가기·현장 작업 따라가기)을
    // 동물이 그 층 사다리 앞에 있다고 치고(고스트) 판단해서, 따라가야 하면 사다리를 타고 그 층으로 간다.
    public class JobGiver_AnimalFollowAcrossLayers : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            using YDMProfiler.Scope __perf = YDMProfiler.Measure(YDMProfiler.Cat.AnimalFollow);
            if (!pawn.Spawned || pawn.Downed || pawn.Faction != Faction.OfPlayer || !pawn.RaceProps.Animal || pawn.InMentalState)
            {
                return null;
            }

            Pawn master = pawn.playerSettings?.RespectedMaster;
            if (master == null || !master.Spawned || master.Map == pawn.Map || !LayerUtility.SameChain(pawn.Map, master.Map))
            {
                return null;
            }

            GameComponent_LayerTravel comp = GameComponent_LayerTravel.Instance;
            if (comp == null || !comp.ScanDue(pawn, typeof(JobGiver_AnimalFollowAcrossLayers)))
            {
                return null;
            }

            IntVec3 cell = LayerUtility.ArrivalCellOn(pawn.Map, master.Map);
            if (!cell.IsValid || !GhostUtility.Run(pawn, master.Map, cell, () => ThinkNode_ConditionalShouldFollowMaster.ShouldFollowMaster(pawn), false))
            {
                return null;
            }

            MapPortal portal = LayerUtility.NextPortalToward(pawn.Map, master.Map);
            if (portal == null || !portal.IsEnterable(out string _) || !pawn.CanReach(portal, PathEndMode.Touch, Danger.Deadly))
            {
                return null;
            }

            Job job = JobMaker.MakeJob(JobDefOf.EnterPortal, portal);
            job.locomotionUrgency = LocomotionUrgency.Jog;
            return job;
        }
    }
}
