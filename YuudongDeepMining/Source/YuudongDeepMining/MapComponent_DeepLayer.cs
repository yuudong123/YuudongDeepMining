using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace YuudongDeepMining
{
    // 지하 층에만 붙는 컴포넌트 (MapGeneratorDef의 customMapComponents). 깊이와 곤충군락 주기를 맡는다.
    public class MapComponent_DeepLayer : CustomMapComponent
    {
        public int depth;

        private int lastInfestationTick = -999999;

        // 확인할 때 정착민이 없어 넘어간 횟수. 정착민이 오면 그만큼의 기회를 한 번에 굴린다 (최대 하루치).
        private int missedChecks;

        private const int CheckInterval = 2500;

        private const int MaxMissedChecks = 24;

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
            if ((ticks + map.uniqueID) % CheckInterval == 0)
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
                Note("cooldown");
                return;
            }

            // 정착민이 이 층에 없으면 군락이 생기지 않는다 (바닐라 곤충군락도 사람 근처에서 생김).
            // 그 기회는 버리지 않고 쌓아 두었다가 정착민이 오면 한꺼번에 굴린다.
            if (!LayerUtility.AnyActiveColonist(map))
            {
                missedChecks = System.Math.Min(missedChecks + 1, MaxMissedChecks);
                Note("no colonists here (" + missedChecks + " checks saved)");
                return;
            }

            float window = CheckInterval * (missedChecks + 1);
            missedChecks = 0;
            float mtb = LayerUtility.InfestationMtbDays(depth);
            if (!Rand.MTBEventOccurs(mtb, GenDate.TicksPerDay, window))
            {
                Note("roll missed (mean " + mtb.ToString("0.#") + " days, window " + (window / GenDate.TicksPerHour).ToString("0") + "h)");
                return;
            }

            Note(TriggerInfestation(ticks) ? "infestation started" : "roll passed but no spot for hives");
        }

        private void Note(string text)
        {
            if (LayerDebug.On)
            {
                LayerDebug.LogOnce("infestation" + map.uniqueID + Find.TickManager.TicksGame, "infestation check on " + LayerUtility.LayerLabel(depth) + ": " + text);
            }
        }

        public bool TriggerInfestation(int ticks)
        {
            if (Faction.OfInsects == null || HiveUtility.TotalSpawnedHivesCount(map) >= 30)
            {
                return false;
            }

            IncidentParms parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
            parms.forced = true;

            // 바닐라 자리 찾기는 정착지 건물 30칸 안만 본다. 지하에서 채굴만 하고 지은 게 없으면 자리가 없으니,
            // 그때는 정착민 근처의 지붕 덮인 칸을 직접 골라 바닐라 사건에 위치로 넘긴다 (생성은 바닐라 그대로).
            if (!InfestationCellFinder.TryFindCell(out IntVec3 _, map))
            {
                IntVec3 spot = FallbackSpot();
                if (!spot.IsValid)
                {
                    return false;
                }

                parms.infestationLocOverride = spot;
            }

            if (IncidentDefOf.Infestation.Worker.TryExecute(parms))
            {
                lastInfestationTick = ticks;
                return true;
            }

            return false;
        }

        // 정착민 근처(6~30칸), 지붕 덮이고 안개가 걷힌 걸을 수 있는 빈 칸.
        private IntVec3 FallbackSpot()
        {
            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned.Where(p => !p.Downed && !p.Dead).ToList();
            if (colonists.Count == 0)
            {
                return IntVec3.Invalid;
            }

            Pawn near = colonists.RandomElement();
            bool Ok(IntVec3 c)
            {
                return c.Standable(map) && !c.Fogged(map) && c.Roofed(map) && c.GetFirstPawn(map) == null && c.GetFirstThing(map, ThingDefOf.Hive) == null
                    && c.GetFirstThing(map, ThingDefOf.TunnelHiveSpawner) == null && c.GetEdifice(map) == null
                    && colonists.All(p => !c.InHorDistOf(p.Position, 6f)) && map.reachability.CanReach(c, near, PathEndMode.Touch, TraverseParms.For(TraverseMode.PassDoors));
            }

            return CellFinder.TryFindRandomCellNear(near.Position, map, 30, Ok, out IntVec3 spot) ? spot : IntVec3.Invalid;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref depth, "depth", 0);
            Scribe_Values.Look(ref lastInfestationTick, "lastInfestationTick", -999999);
            Scribe_Values.Look(ref missedChecks, "missedChecks", 0);
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
