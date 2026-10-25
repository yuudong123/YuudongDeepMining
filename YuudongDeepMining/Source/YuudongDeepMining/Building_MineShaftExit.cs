using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace YuudongDeepMining
{
    // 지하 층에서 위로 올라가는 사다리. 부서지지 않는다.
    [StaticConstructorOnStartup]
    public class Building_MineShaftExit : PocketMapExit
    {
        private static readonly Texture2D CollapseTex = ContentFinder<Texture2D>.Get("UI/Designators/Cancel");

        public int Depth => LayerUtility.DepthOf(Map);

        public override string EnterString => "YDM.GoUp".Translate(LayerUtility.LayerLabel(Depth - 1));

        public override string EnteringString => "YDM.GoingUp".Translate(LayerUtility.LayerLabel(Depth - 1));

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            if (entrance is Building_MineShaft shaft)
            {
                yield return new Command_Action
                {
                    defaultLabel = "YDM.Collapse".Translate(),
                    defaultDesc = "YDM.CollapseDesc".Translate(),
                    icon = CollapseTex,
                    action = shaft.ConfirmCollapse,
                };
            }
        }

        public override string GetInspectString()
        {
            StringBuilder sb = new StringBuilder(base.GetInspectString());
            if (sb.Length > 0)
            {
                sb.AppendLine();
            }

            int depth = Depth;
            sb.AppendLine("YDM.InspectDepth".Translate(LayerUtility.LayerLabel(depth)));
            sb.Append("YDM.InspectHardness".Translate(LayerUtility.HardnessAt(depth).ToStringPercent()));
            if (DeepMiningMod.Settings.infestations)
            {
                sb.AppendLine();
                sb.Append("YDM.InspectInfestation".Translate(LayerUtility.InfestationMtbDays(depth).ToString("0.#")));
            }

            return sb.ToString();
        }
    }
}
