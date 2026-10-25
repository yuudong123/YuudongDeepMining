using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace YuudongDeepMining
{
    // 지하 층에서 위로 올라가는 계단. 부서지지 않는다.
    // 이 층에 보관할 곳이 없는 물건 중 '저장소' 탭에서 고른 것은 운반꾼이 이 계단으로 올려 보낸다.
    [StaticConstructorOnStartup]
    public class Building_MineShaftExit : PocketMapExit, IStoreSettingsParent
    {
        private static readonly Texture2D CollapseTex = ContentFinder<Texture2D>.Get("UI/Designators/Cancel");

        private static readonly Texture2D HaulUpTex = ContentFinder<Texture2D>.Get("UI/Commands/ExitCave");

        private StorageSettings haulUpSettings;

        public bool autoHaulUp = true;

        public int Depth => LayerUtility.DepthOf(Map);

        public override string EnterString => "YDM.GoUp".Translate(LayerUtility.LayerLabel(Depth - 1));

        public override string EnteringString => "YDM.GoingUp".Translate(LayerUtility.LayerLabel(Depth - 1));

        public bool StorageTabVisible => true;

        public StorageSettings GetStoreSettings()
        {
            EnsureSettings();
            return haulUpSettings;
        }

        public StorageSettings GetParentStoreSettings()
        {
            return null;
        }

        public void Notify_SettingsChanged()
        {
        }

        private void EnsureSettings()
        {
            if (haulUpSettings != null)
            {
                return;
            }

            haulUpSettings = new StorageSettings(this);
            haulUpSettings.filter.SetAllow(ThingCategoryDefOf.ResourcesRaw, true);
            if (ThingDefOf.ComponentIndustrial != null)
            {
                haulUpSettings.filter.SetAllow(ThingDefOf.ComponentIndustrial, true);
            }
        }

        public bool WantsHaulUp(Thing thing)
        {
            if (!autoHaulUp || entrance == null || !entrance.Spawned)
            {
                return false;
            }

            EnsureSettings();
            return haulUpSettings.AllowedToAccept(thing);
        }

        public override void PostMake()
        {
            base.PostMake();
            EnsureSettings();
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            yield return new Command_Toggle
            {
                defaultLabel = "YDM.AutoHaulUp".Translate(),
                defaultDesc = "YDM.AutoHaulUpDesc".Translate(),
                icon = HaulUpTex,
                isActive = () => autoHaulUp,
                toggleAction = delegate { autoHaulUp = !autoHaulUp; },
            };

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

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref autoHaulUp, "autoHaulUp", true);
            Scribe_Deep.Look(ref haulUpSettings, "haulUpSettings", this);
        }
    }
}
