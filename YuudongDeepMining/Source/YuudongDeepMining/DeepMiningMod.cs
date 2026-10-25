using HarmonyLib;
using UnityEngine;
using Verse;

namespace YuudongDeepMining
{
    public class DeepMiningSettings : ModSettings
    {
        public int maxDepth = 5;
        public int mapSize = 150;

        // 층마다 바위·광석이 받는 피해가 1 / (1 + hardnessPerLayer * 깊이)로 줄어든다. hardnessCapDepth 층부터는 그대로.
        public float hardnessPerLayer = 0.5f;
        public int hardnessCapDepth = 4;

        // 지하 1층의 곤충군락 평균 간격(일). 한 층 내려갈 때마다 infestationLayerFactor를 곱한다. infestationCapDepth 층부터는 그대로.
        public bool infestations = true;
        public float infestationBaseMtbDays = 15f;
        public float infestationLayerFactor = 0.75f;
        public int infestationCapDepth = 4;
        public float infestationCooldownDays = 3f;

        public bool smartRaidersDescend = true;
        public bool shaftInvulnerable;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref maxDepth, "maxDepth", 5);
            Scribe_Values.Look(ref mapSize, "mapSize", 150);
            Scribe_Values.Look(ref hardnessPerLayer, "hardnessPerLayer", 0.5f);
            Scribe_Values.Look(ref hardnessCapDepth, "hardnessCapDepth", 4);
            Scribe_Values.Look(ref infestations, "infestations", true);
            Scribe_Values.Look(ref infestationBaseMtbDays, "infestationBaseMtbDays", 15f);
            Scribe_Values.Look(ref infestationLayerFactor, "infestationLayerFactor", 0.75f);
            Scribe_Values.Look(ref infestationCapDepth, "infestationCapDepth", 4);
            Scribe_Values.Look(ref infestationCooldownDays, "infestationCooldownDays", 3f);
            Scribe_Values.Look(ref smartRaidersDescend, "smartRaidersDescend", true);
            Scribe_Values.Look(ref shaftInvulnerable, "shaftInvulnerable", false);
        }
    }

    public class DeepMiningMod : Mod
    {
        public static DeepMiningSettings Settings;

        public DeepMiningMod(ModContentPack content)
            : base(content)
        {
            Settings = GetSettings<DeepMiningSettings>();
            new Harmony("yuudong2.deepmining").PatchAll(typeof(DeepMiningMod).Assembly);
        }

        public override string SettingsCategory()
        {
            return "YDM.SettingsCategory".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            DeepMiningSettings s = Settings;
            Listing_Standard list = new Listing_Standard();
            list.Begin(inRect);

            s.maxDepth = Mathf.RoundToInt(list.SliderLabeled("YDM.Settings.MaxDepth".Translate(s.maxDepth), s.maxDepth, 1f, 10f));
            s.mapSize = Mathf.RoundToInt(list.SliderLabeled("YDM.Settings.MapSize".Translate(s.mapSize), s.mapSize, 75f, 250f) / 25f) * 25;
            list.Gap();

            s.hardnessPerLayer = Mathf.Round(list.SliderLabeled("YDM.Settings.HardnessPerLayer".Translate(s.hardnessPerLayer.ToStringPercent()), s.hardnessPerLayer, 0f, 2f) * 20f) / 20f;
            s.hardnessCapDepth = Mathf.RoundToInt(list.SliderLabeled("YDM.Settings.HardnessCap".Translate(s.hardnessCapDepth), s.hardnessCapDepth, 1f, 10f));
            list.Label("YDM.Settings.HardnessExample".Translate(LayerUtility.HardnessAt(1).ToStringPercent(), LayerUtility.HardnessAt(s.hardnessCapDepth).ToStringPercent()));
            list.Gap();

            list.CheckboxLabeled("YDM.Settings.Infestations".Translate(), ref s.infestations);
            if (s.infestations)
            {
                s.infestationBaseMtbDays = Mathf.Round(list.SliderLabeled("YDM.Settings.InfestationBase".Translate(s.infestationBaseMtbDays.ToString("0")), s.infestationBaseMtbDays, 2f, 60f));
                s.infestationLayerFactor = Mathf.Round(list.SliderLabeled("YDM.Settings.InfestationFactor".Translate(s.infestationLayerFactor.ToStringPercent()), s.infestationLayerFactor, 0.3f, 1f) * 20f) / 20f;
                s.infestationCapDepth = Mathf.RoundToInt(list.SliderLabeled("YDM.Settings.InfestationCap".Translate(s.infestationCapDepth), s.infestationCapDepth, 1f, 10f));
                s.infestationCooldownDays = Mathf.Round(list.SliderLabeled("YDM.Settings.InfestationCooldown".Translate(s.infestationCooldownDays.ToString("0.#")), s.infestationCooldownDays, 0f, 15f) * 2f) / 2f;
                list.Label("YDM.Settings.InfestationExample".Translate(LayerUtility.InfestationMtbDays(1).ToString("0.#"), s.infestationCapDepth, LayerUtility.InfestationMtbDays(s.infestationCapDepth).ToString("0.#")));
            }

            list.Gap();
            list.CheckboxLabeled("YDM.Settings.SmartRaiders".Translate(), ref s.smartRaidersDescend, "YDM.Settings.SmartRaidersDesc".Translate());
            list.CheckboxLabeled("YDM.Settings.ShaftInvulnerable".Translate(), ref s.shaftInvulnerable, "YDM.Settings.ShaftInvulnerableDesc".Translate());
            list.End();
        }
    }
}
