using System.Linq;
using RimWorld;
using Verse;

namespace YuudongDeepMining
{
    // 지상 갱도는 지상(지하 층이 아닌 지도)에만, 지하 갱도는 최대 깊이보다 위인 지하 층에만 짓는다. 지도마다 갱도는 하나.
    public class PlaceWorker_MineShaft : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            ThingDef def = checkingDef as ThingDef;
            bool isLayer = LayerUtility.IsLayer(map);
            if (def == YDMDefOf.YDM_MineShaft && isLayer)
            {
                return "YDM.SurfaceOnly".Translate();
            }

            if (def == YDMDefOf.YDM_MineShaftDeep)
            {
                if (!isLayer)
                {
                    return "YDM.UndergroundOnly".Translate();
                }

                if (LayerUtility.DepthOf(map) >= DeepMiningMod.Settings.maxDepth)
                {
                    return "YDM.MaxDepthReached".Translate(DeepMiningMod.Settings.maxDepth);
                }
            }

            if (AlreadyHasShaft(map, thingToIgnore))
            {
                return "YDM.OneShaftPerMap".Translate();
            }

            return AcceptanceReport.WasAccepted;
        }

        private static bool AlreadyHasShaft(Map map, Thing ignore)
        {
            foreach (Thing t in map.listerThings.ThingsOfDef(YDMDefOf.YDM_MineShaft).Concat(map.listerThings.ThingsOfDef(YDMDefOf.YDM_MineShaftDeep)))
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
