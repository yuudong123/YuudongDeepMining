using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace YuudongDeepMining
{
    // 지상에 정착민이 없고 지하에만 있을 때, 공병(Sappers)·돌파(Breaching) 습격대는 갱도를 찾아 내려온다.
    // 다른 습격대는 지하를 알아채지 못한다 (바닐라 그대로).
    public static class RaidDescentUtility
    {
        private static readonly AccessTools.FieldRef<LordJob_AssaultColony, bool> SappersRef = AccessTools.FieldRefAccess<LordJob_AssaultColony, bool>("sappers");

        private static readonly AccessTools.FieldRef<LordJob_AssaultColony, bool> BreachersRef = AccessTools.FieldRefAccess<LordJob_AssaultColony, bool>("breachers");

        // 갱도로 내려간 적이 원래 어떤 전략이었는지 (저장하지 않음: 불러오면 공병으로 친다).
        private static readonly Dictionary<Pawn, bool> breacherPawns = new Dictionary<Pawn, bool>();

        private static readonly List<Pawn> tmpPawns = new List<Pawn>();

        public static bool IsSmartAssault(Lord lord, out bool breachers)
        {
            breachers = false;
            if (!(lord?.LordJob is LordJob_AssaultColony assault))
            {
                return false;
            }

            breachers = BreachersRef(assault);
            return SappersRef(assault) || breachers;
        }

        private static bool IsHostileRaider(Pawn pawn)
        {
            return pawn.Spawned && !pawn.Dead && !pawn.Downed && pawn.Faction != null && pawn.Faction != Faction.OfPlayer
                && pawn.Faction.HostileTo(Faction.OfPlayer) && !pawn.IsPrisoner && (pawn.RaceProps.Humanlike || pawn.RaceProps.IsMechanoid);
        }

        public static void TrySendRaidersDown(Map map)
        {
            Building_MineShaft shaft = LayerUtility.DownShaft(map);
            if (shaft == null || !shaft.PocketMapExists || LayerUtility.AnyActiveColonist(map) || !LayerUtility.ColonistsBelow(map))
            {
                return;
            }

            foreach (Lord lord in map.lordManager.lords.ToList())
            {
                if (lord.faction == null || !lord.faction.HostileTo(Faction.OfPlayer) || !IsSmartAssault(lord, out bool breachers))
                {
                    continue;
                }

                tmpPawns.Clear();
                foreach (Pawn pawn in lord.ownedPawns)
                {
                    if (IsHostileRaider(pawn) && pawn.CanReach(shaft, PathEndMode.Touch, Danger.Deadly))
                    {
                        tmpPawns.Add(pawn);
                    }
                }

                // 갱도까지 길이 열린 적이 생기면 함께 내려간다. 아직 길이 없으면 원래대로 벽을 뚫으며 다가온다.
                if (tmpPawns.Count == 0)
                {
                    continue;
                }

                SendDown(map, lord.faction, tmpPawns, shaft, breachers);
                if (lord.ownedPawns.Count == 0)
                {
                    map.lordManager.RemoveLord(lord);
                }
            }

            tmpPawns.Clear();
        }

        private static void SendDown(Map map, Faction faction, List<Pawn> pawns, Building_MineShaft shaft, bool breachers)
        {
            List<Pawn> list = pawns.ToList();
            foreach (Pawn pawn in list)
            {
                pawn.GetLord()?.RemovePawn(pawn);
                breacherPawns[pawn] = breachers;
            }

            LordMaker.MakeNewLord(faction, new LordJob_DescendShaft(shaft, breachers), map, list);
            if (map == Find.CurrentMap || LayerUtility.MapsBelow(map).Contains(Find.CurrentMap))
            {
                Messages.Message("YDM.RaidersDescending".Translate(faction.Name), new TargetInfo(shaft.Position, map), MessageTypeDefOf.ThreatBig);
            }
        }

        // 갱도로 내려와 지휘가 없는 적을 모아, 이 층에 정착민이 있으면 공격하고 없으면 더 내려간다.
        public static void RegroupArrivedHostiles(Map map)
        {
            tmpPawns.Clear();
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (IsHostileRaider(pawn) && pawn.GetLord() == null)
                {
                    tmpPawns.Add(pawn);
                }
            }

            if (tmpPawns.Count == 0)
            {
                return;
            }

            foreach (IGrouping<Faction, Pawn> group in tmpPawns.GroupBy(p => p.Faction).ToList())
            {
                List<Pawn> pawns = group.ToList();
                bool breachers = pawns.Any(p => breacherPawns.TryGetValue(p, out bool b) && b);
                Building_MineShaft shaft = LayerUtility.DownShaft(map);
                if (!LayerUtility.AnyActiveColonist(map) && shaft != null && shaft.PocketMapExists && LayerUtility.ColonistsBelow(map))
                {
                    LordMaker.MakeNewLord(group.Key, new LordJob_DescendShaft(shaft, breachers), map, pawns);
                }
                else
                {
                    LordMaker.MakeNewLord(group.Key, new LordJob_AssaultColony(group.Key, canKidnap: true, canTimeoutOrFlee: true, sappers: !breachers, useAvoidGridSmart: true, canSteal: true, breachers: breachers), map, pawns);
                }
            }

            tmpPawns.Clear();
        }

        public static void CleanupEmptyLords(Map map)
        {
            List<Lord> lords = map.lordManager.lords;
            for (int i = lords.Count - 1; i >= 0; i--)
            {
                Lord lord = lords[i];
                if (!(lord.LordJob is LordJob_DescendShaft descend))
                {
                    continue;
                }

                if (lord.ownedPawns.Count == 0)
                {
                    map.lordManager.RemoveLord(lord);
                    continue;
                }

                // 갱도가 없어졌으면 이 지도에서 그대로 공격한다.
                if (descend.shaft == null || !descend.shaft.Spawned || descend.shaft.Map != map)
                {
                    List<Pawn> pawns = lord.ownedPawns.ToList();
                    Faction faction = lord.faction;
                    map.lordManager.RemoveLord(lord);
                    LordMaker.MakeNewLord(faction, new LordJob_AssaultColony(faction, canKidnap: true, canTimeoutOrFlee: true, sappers: !descend.breachers, useAvoidGridSmart: true, canSteal: true, breachers: descend.breachers), map, pawns);
                }
            }

            if (breacherPawns.Count > 200)
            {
                foreach (Pawn pawn in breacherPawns.Keys.Where(p => p.Dead || p.Destroyed).ToList())
                {
                    breacherPawns.Remove(pawn);
                }
            }
        }
    }

    public class LordJob_DescendShaft : LordJob
    {
        public Building_MineShaft shaft;

        public bool breachers;

        public LordJob_DescendShaft()
        {
        }

        public LordJob_DescendShaft(Building_MineShaft shaft, bool breachers)
        {
            this.shaft = shaft;
            this.breachers = breachers;
        }

        public override bool AddFleeToil => false;

        public override StateGraph CreateGraph()
        {
            StateGraph graph = new StateGraph();
            graph.AddToil(new LordToil_DescendShaft());
            return graph;
        }

        public override void ExposeData()
        {
            Scribe_References.Look(ref shaft, "shaft");
            Scribe_Values.Look(ref breachers, "breachers", false);
        }
    }

    public class LordToil_DescendShaft : LordToil
    {
        public override bool AllowSatisfyLongNeeds => false;

        public override void UpdateAllDuties()
        {
            Building_MineShaft shaft = (lord.LordJob as LordJob_DescendShaft)?.shaft;
            foreach (Pawn pawn in lord.ownedPawns)
            {
                pawn.mindState.duty = new PawnDuty(YDMDefOf.YDM_EnterShaft, shaft);
            }
        }
    }
}
