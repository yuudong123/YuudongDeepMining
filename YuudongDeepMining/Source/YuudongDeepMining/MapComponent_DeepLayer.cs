using RimWorld;
using Verse;

namespace YuudongDeepMining
{
    // 지하 층에만 붙는 컴포넌트 (MapGeneratorDef의 customMapComponents). 깊이와 곤충군락 주기를 맡는다.
    public class MapComponent_DeepLayer : CustomMapComponent
    {
        public int depth;

        private int lastInfestationTick = -999999;

        public MapComponent_DeepLayer(Map map)
            : base(map)
        {
            if (LayerUtility.generatingDepth > 0)
            {
                depth = LayerUtility.generatingDepth;
            }
        }

        public override void MapComponentTick()
        {
            using YDMProfiler.Scope __perf = YDMProfiler.Measure(YDMProfiler.Cat.Infestation);
            int ticks = Find.TickManager.TicksGame;
            if ((ticks + map.uniqueID) % 2500 == 0)
            {
                TryInfestation(ticks);
            }
        }

        private void TryInfestation(int ticks)
        {
            DeepMiningSettings s = DeepMiningMod.Settings;
            if (!s.infestations || depth <= 0 || Faction.OfInsects == null)
            {
                return;
            }

            if (ticks - lastInfestationTick < s.infestationCooldownDays * GenDate.TicksPerDay)
            {
                return;
            }

            // 정착민이 이 층에 없으면 군락이 생기지 않는다 (바닐라 곤충군락도 사람 근처에서 생김).
            if (!LayerUtility.AnyActiveColonist(map))
            {
                return;
            }

            if (!Rand.MTBEventOccurs(LayerUtility.InfestationMtbDays(depth), GenDate.TicksPerDay, 2500f))
            {
                return;
            }

            TriggerInfestation(ticks);
        }

        public bool TriggerInfestation(int ticks)
        {
            if (HiveUtility.TotalSpawnedHivesCount(map) >= 30 || !InfestationCellFinder.TryFindCell(out IntVec3 _, map))
            {
                return false;
            }

            IncidentParms parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
            parms.forced = true;
            if (IncidentDefOf.Infestation.Worker.TryExecute(parms))
            {
                lastInfestationTick = ticks;
                return true;
            }

            return false;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref depth, "depth", 0);
            Scribe_Values.Look(ref lastInfestationTick, "lastInfestationTick", -999999);
        }
    }

    // 모든 지도에 붙는다. 갱도가 있는 지도에서만 일한다: 영리한 습격대를 갱도로 내려보내고, 내려온 적에게 다시 지휘를 붙인다.
    public class MapComponent_DeepRaids : MapComponent
    {
        public MapComponent_DeepRaids(Map map)
            : base(map)
        {
        }

        public override void MapComponentTick()
        {
            using YDMProfiler.Scope __perf = YDMProfiler.Measure(YDMProfiler.Cat.Raids);
            if ((Find.TickManager.TicksGame + map.uniqueID) % 250 != 0)
            {
                return;
            }

            if (LayerUtility.IsLayer(map))
            {
                RaidDescentUtility.RegroupArrivedHostiles(map);
            }

            if (DeepMiningMod.Settings.smartRaidersDescend && LayerUtility.DownShaft(map) != null)
            {
                RaidDescentUtility.TrySendRaidersDown(map);
            }

            RaidDescentUtility.CleanupEmptyLords(map);
        }
    }
}
