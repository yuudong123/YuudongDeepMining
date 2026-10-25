using System.Linq;
using RimWorld;
using Verse;

namespace YuudongDeepMining
{
    // 갱도는 지도마다 하나. 지하 층에서는 최대 깊이보다 위인 층에만 짓는다.
    public class PlaceWorker_MineShaft : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            // 지상이든 지하 층이든 지을 수 있다. 지하 층에서는 최대 깊이보다 위일 때만.
            if (LayerUtility.IsLayer(map) && LayerUtility.DepthOf(map) >= DeepMiningMod.Settings.maxDepth)
            {
                return "YDM.MaxDepthReached".Translate(DeepMiningMod.Settings.maxDepth);
            }

            if (AlreadyHasShaft(map, thingToIgnore))
            {
                return "YDM.OneShaftPerMap".Translate();
            }

            return AcceptanceReport.WasAccepted;
        }

        private static bool AlreadyHasShaft(Map map, Thing ignore)
        {
            foreach (Thing t in map.listerThings.ThingsOfDef(YDMDefOf.YDM_MineShaft))
            {
                if (t != ignore)
                {
                    return true;
                }
            }

            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).Concat(map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame)))
            {
                if (t != ignore && t.def.entityDefToBuild is ThingDef built && LayerUtility.IsShaftDef(built))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
