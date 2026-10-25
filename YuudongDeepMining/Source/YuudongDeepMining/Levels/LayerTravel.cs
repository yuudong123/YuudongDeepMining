using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace YuudongDeepMining
{
    public enum TravelKind
    {
        // 다른 층에 일이 있어서 간다.
        Work,
        // 한가해져서 주로 머무는 층으로 돌아간다.
        Home,
        // 물건을 들고 다른 층 창고로 간다.
        Haul,
        // 플레이어가 다른 층에서 내린 명령.
        Remote,
        // 이 층에서는 채울 수 없는 욕구(잠·식사·오락 등)를 다른 층에서 채우러 간다.
        Need,
    }

    public class TravelOrder : IExposable
    {
        public Map target;
        public TravelKind kind;

        // 원격 명령: 도착하면 이 칸에서 같은 이름의 우클릭 명령을 고른다. move면 그 칸으로 이동만 한다.
        public string label;
        public IntVec3 cell = IntVec3.Invalid;
        public bool move;

        // 사람을 옮길 때: 도착하면 이 침대로 바닐라 구조(또는 포획) 작업을 시킨다.
        public Thing bed;
        public bool capture;

        // 재료를 옮길 때: 도착하면 이 작업대 옆에 내려놓는다.
        public Thing destination;

        public void ExposeData()
        {
            Scribe_References.Look(ref target, "target");
            Scribe_Values.Look(ref kind, "kind");
            Scribe_Values.Look(ref label, "label");
            Scribe_Values.Look(ref cell, "cell", IntVec3.Invalid);
            Scribe_Values.Look(ref move, "move", false);
            Scribe_References.Look(ref bed, "bed");
            Scribe_Values.Look(ref capture, "capture", false);
            Scribe_References.Look(ref destination, "destination");
        }
    }

    // 정착민의 '주로 머무는 층'과 층 이동 명령을 관리한다.
    public class GameComponent_LayerTravel : GameComponent
    {
        public const int Free = -1;

        // 다른 층 일거리를 다시 확인하기까지의 최소 간격 (계산을 줄이려는 것이지 층 이동을 막는 것이 아니다).
        private const int ScanInterval = 120;

        // 일하러 갔는데 일이 없었던 층은 잠깐 건너뛴다 (헛걸음 기억).
        private const int WastedTicks = 2500;

        private Dictionary<Pawn, int> homes = new Dictionary<Pawn, int>();
        private Dictionary<Pawn, TravelOrder> orders = new Dictionary<Pawn, TravelOrder>();

        private List<Pawn> tmpKeys;
        private List<int> tmpInts;
        private List<Pawn> tmpKeys2;
        private List<TravelOrder> tmpOrders;

        private readonly Dictionary<Pawn, Job> issuedRemoteJobs = new Dictionary<Pawn, Job>();
        private readonly Dictionary<(Pawn, object), int> lastScan = new Dictionary<(Pawn, object), int>();
        private readonly Dictionary<Pawn, KeyValuePair<Map, int>> workArrivals = new Dictionary<Pawn, KeyValuePair<Map, int>>();
        private readonly Dictionary<Pawn, Dictionary<Map, int>> wasted = new Dictionary<Pawn, Dictionary<Map, int>>();

        public GameComponent_LayerTravel(Game game)
        {
        }

        public static GameComponent_LayerTravel Instance => Current.Game?.GetComponent<GameComponent_LayerTravel>();

        public int GetHome(Pawn pawn)
        {
            return pawn != null && homes.TryGetValue(pawn, out int depth) ? depth : Free;
        }

        public void SetHome(Pawn pawn, int depth)
        {
            if (depth == Free)
            {
                homes.Remove(pawn);
            }
            else
            {
                homes[pawn] = depth;
            }
        }

        public TravelOrder GetOrder(Pawn pawn)
        {
            return pawn != null && orders.TryGetValue(pawn, out TravelOrder order) ? order : null;
        }

        public void SetOrder(Pawn pawn, TravelOrder order)
        {
            orders[pawn] = order;
            issuedRemoteJobs.Remove(pawn);
        }

        public void ClearOrder(Pawn pawn)
        {
            orders.Remove(pawn);
            issuedRemoteJobs.Remove(pawn);
        }

        // 같은 림·같은 판단(작업, 또는 욕구 노드 종류)마다 다른 층 확인 간격을 둔다.
        public bool ScanDue(Pawn pawn, object key)
        {
            int now = Find.TickManager.TicksGame;
            if (lastScan.TryGetValue((pawn, key), out int last) && now - last < ScanInterval)
            {
                return false;
            }

            if (lastScan.Count > 4096)
            {
                lastScan.Clear();
            }

            lastScan[(pawn, key)] = now;
            return true;
        }

        public void NoteWorkArrival(Pawn pawn, Map map)
        {
            workArrivals[pawn] = new KeyValuePair<Map, int>(map, Find.TickManager.TicksGame);
        }

        // 일하러 왔는데 도착 뒤 일을 하나도 못 하고 한가해졌으면 이 층을 잠깐 건너뛴다.
        public void CheckWastedTrip(Pawn pawn)
        {
            if (!workArrivals.TryGetValue(pawn, out KeyValuePair<Map, int> arrival))
            {
                return;
            }

            workArrivals.Remove(pawn);
            JobTag tag = pawn.mindState.lastJobTag;
            bool didWork = tag == JobTag.MiscWork || tag == JobTag.Fieldwork;
            if (arrival.Key == pawn.Map && !didWork && Find.TickManager.TicksGame - arrival.Value < 1200)
            {
                if (!wasted.TryGetValue(pawn, out Dictionary<Map, int> byMap))
                {
                    wasted[pawn] = byMap = new Dictionary<Map, int>();
                }

                byMap[arrival.Key] = Find.TickManager.TicksGame + WastedTicks;
            }
        }

        public bool IsWasted(Pawn pawn, Map map)
        {
            return wasted.TryGetValue(pawn, out Dictionary<Map, int> byMap) && byMap.TryGetValue(map, out int until) && Find.TickManager.TicksGame < until;
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % 20 != 0 || orders.Count == 0)
            {
                return;
            }

            foreach (KeyValuePair<Pawn, TravelOrder> kv in orders.ToList())
            {
                Pawn pawn = kv.Key;
                TravelOrder order = kv.Value;
                if (pawn == null || pawn.Dead || pawn.Destroyed || order.target == null || order.target.Disposed)
                {
                    ClearOrder(pawn);
                    continue;
                }

                if (order.kind == TravelKind.Remote && pawn.Spawned)
                {
                    TickRemote(pawn, order);
                }
            }
        }

        // 원격 명령: 사다리를 한 층씩 지나 대상 층에 오면 명령을 실행하고, 그 층을 보고 있으면 다시 선택한다.
        private void TickRemote(Pawn pawn, TravelOrder order)
        {
            if (pawn.Map == order.target)
            {
                ClearOrder(pawn);
                RemoteControl.ExecuteOnArrival(pawn, order);
                return;
            }

            if (!LayerUtility.SameChain(pawn.Map, order.target))
            {
                ClearOrder(pawn);
                return;
            }

            Job cur = pawn.CurJob;
            issuedRemoteJobs.TryGetValue(pawn, out Job issued);
            if (cur != null && cur == issued)
            {
                return;
            }

            // 가는 도중 플레이어가 다른 명령을 내렸으면 원격 명령을 취소한다.
            if (issued != null && cur != null && cur.playerForced && cur.def != JobDefOf.EnterPortal)
            {
                ClearOrder(pawn);
                return;
            }

            if (pawn.Downed || pawn.InMentalState)
            {
                return;
            }

            MapPortal portal = LayerUtility.NextPortalToward(pawn.Map, order.target);
            if (portal == null)
            {
                ClearOrder(pawn);
                return;
            }

            Job job = JobMaker.MakeJob(JobDefOf.EnterPortal, portal);
            if (pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc))
            {
                issuedRemoteJobs[pawn] = job;
            }
        }

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                homes.RemoveAll(kv => kv.Key == null || kv.Key.Dead || kv.Key.Destroyed);
                orders.RemoveAll(kv => kv.Key == null || kv.Key.Dead || kv.Key.Destroyed || kv.Value?.target == null);
            }

            Scribe_Collections.Look(ref homes, "assigned", LookMode.Reference, LookMode.Value, ref tmpKeys, ref tmpInts);
            Scribe_Collections.Look(ref orders, "orders", LookMode.Reference, LookMode.Deep, ref tmpKeys2, ref tmpOrders);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                homes ??= new Dictionary<Pawn, int>();
                orders ??= new Dictionary<Pawn, TravelOrder>();
                homes.RemoveAll(kv => kv.Key == null);
                orders.RemoveAll(kv => kv.Key == null || kv.Value?.target == null);
            }
        }
    }

    [StaticConstructorOnStartup]
    public static class LayerHomeGizmo
    {
        private static readonly Texture2D Icon = ContentFinder<Texture2D>.Get("UI/Commands/EnterCave");

        public static string HomeLabel(int depth)
        {
            return depth == GameComponent_LayerTravel.Free ? "YDM.AssignFree".Translate().ToString() : LayerUtility.LayerLabel(depth);
        }

        public static Gizmo Make(Pawn pawn)
        {
            GameComponent_LayerTravel comp = GameComponent_LayerTravel.Instance;
            int current = comp?.GetHome(pawn) ?? GameComponent_LayerTravel.Free;
            return new Command_Action
            {
                defaultLabel = "YDM.AssignLabel".Translate(HomeLabel(current)),
                defaultDesc = "YDM.AssignDesc".Translate(),
                icon = Icon,
                groupKey = 77310421,
                action = delegate
                {
                    List<FloatMenuOption> options = new List<FloatMenuOption>();
                    int deepest = LayerUtility.DeepestLayerInGame();
                    for (int depth = GameComponent_LayerTravel.Free; depth <= deepest; depth++)
                    {
                        int chosen = depth;
                        options.Add(new FloatMenuOption(HomeLabel(chosen), delegate
                        {
                            foreach (Pawn selected in Find.Selector.SelectedPawns.Where(p => p.IsColonistPlayerControlled).DefaultIfEmpty(pawn))
                            {
                                comp?.SetHome(selected, chosen);
                            }
                        }));
                    }

                    Find.WindowStack.Add(new FloatMenu(options));
                },
            };
        }
    }

    public static class LayerTravelUtility
    {
        public static bool CanUseLayers(Pawn pawn)
        {
            Map map = pawn.Map;
            return map != null && !pawn.Drafted && !pawn.Downed && pawn.IsColonistPlayerControlled && !pawn.InMentalState
                && (LayerUtility.IsLayer(map) || LayerUtility.DownShaft(map) != null);
        }

        public static Job EnterJob(Pawn pawn, MapPortal portal)
        {
            if (portal == null || !portal.Spawned || !portal.IsEnterable(out string _) || !pawn.CanReach(portal, PathEndMode.Touch, Danger.Some))
            {
                return null;
            }

            Job job = JobMaker.MakeJob(JobDefOf.EnterPortal, portal);
            job.locomotionUrgency = LocomotionUrgency.Jog;
            return job;
        }

        // 배고프거나 졸리면 지금 층에서 먼저 해결한다.
        public static bool HasUrgentNeed(Pawn pawn)
        {
            Need_Food food = pawn.needs?.food;
            if (food != null && food.CurCategory >= HungerCategory.Hungry)
            {
                return true;
            }

            Need_Rest rest = pawn.needs?.rest;
            return rest != null && rest.CurCategory >= RestCategory.Tired;
        }

        // 바닐라 작업 탐색과 같은 순서로 일의 순위를 매긴다 (작은 수가 먼저).
        // 바닐라는 우선순위 순으로 정렬된 작업 목록을 앞에서부터 보며, priorityInType이 같은 연속 묶음 안에서는 가까운 일을 고른다.
        // 그래서 순위는 그 일을 낸 작업이 속한 묶음의 시작 위치다. 어느 작업에서 나온 일인지 모르면 0(가장 앞)으로 쳐서 그대로 둔다.
        public static int Rank(Pawn pawn, Job job)
        {
            if (job == null)
            {
                return int.MaxValue;
            }

            WorkGiverDef def = job.workGiverDef;
            List<WorkGiver> list = pawn.workSettings?.WorkGiversInOrderNormal;
            if (def == null || list == null)
            {
                return 0;
            }

            int index = list.FindIndex(g => g.def == def);
            if (index < 0)
            {
                return 0;
            }

            while (index > 0 && list[index - 1].def.priorityInType == list[index].def.priorityInType)
            {
                index--;
            }

            return index;
        }

        // 바닐라 작업 탐색을 다른 층 사다리 앞에서 해 본다 (고스트 판정).
        public static Job ProbeWorkOn(JobGiver_Work giver, Pawn pawn, Map target)
        {
            IntVec3 cell = LayerUtility.ArrivalCellOn(pawn.Map, target);
            if (!cell.IsValid)
            {
                return null;
            }

            return GhostUtility.Run(pawn, target, cell, () => giver.TryIssueJobPackage(pawn, default(JobIssueParams)).Job, null);
        }
    }

    // 생각 트리 앞쪽(Humanlike_PreMain, 일·욕구보다 먼저): 진행 중인 층 이동(일·욕구·집·운반)을 한 층씩 이어 간다.
    public class JobGiver_LayerTravel : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!LayerTravelUtility.CanUseLayers(pawn))
            {
                return null;
            }

            Map map = pawn.Map;
            GameComponent_LayerTravel comp = GameComponent_LayerTravel.Instance;
            TravelOrder order = comp?.GetOrder(pawn);
            if (order != null && order.kind == TravelKind.Remote)
            {
                return null;
            }

            if (order != null && order.kind == TravelKind.Haul)
            {
                return HaulAcrossUtility.ContinueHaul(pawn, order, comp);
            }

            if (order == null)
            {
                return null;
            }

            if (pawn.Map == order.target)
            {
                comp.ClearOrder(pawn);
                if (order.kind == TravelKind.Work)
                {
                    comp.NoteWorkArrival(pawn, map);
                }

                return null;
            }

            // 욕구를 채우러 가는 길이 아니면, 배고프거나 졸릴 때는 바닐라 욕구 판단에 먼저 맡긴다.
            if (order.kind != TravelKind.Need && LayerTravelUtility.HasUrgentNeed(pawn))
            {
                return null;
            }

            Job job = LayerTravelUtility.EnterJob(pawn, LayerUtility.NextPortalToward(map, order.target));
            if (job == null)
            {
                comp.ClearOrder(pawn);
            }

            return job;
        }
    }

    // 생각 트리 뒤쪽(Humanlike_PostMain, 어느 층에도 할 일이 없을 때): 주로 머무는 층으로 돌아간다.
    // 다른 층 일거리로 가는 판단은 바닐라 작업 탐색 패치(JobGiver_Work_LayerWork_Patch)가 한다.
    public class JobGiver_LayerIdle : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!LayerTravelUtility.CanUseLayers(pawn) || LayerTravelUtility.HasUrgentNeed(pawn))
            {
                return null;
            }

            GameComponent_LayerTravel comp = GameComponent_LayerTravel.Instance;
            if (comp == null || comp.GetOrder(pawn) != null)
            {
                return null;
            }

            Map map = pawn.Map;
            comp.CheckWastedTrip(pawn);
            int here = LayerUtility.DepthOf(map);
            int home = comp.GetHome(pawn);
            if (home == GameComponent_LayerTravel.Free || home == here)
            {
                return null;
            }

            Map homeMap = LayerUtility.Chain(map).FirstOrDefault(m => LayerUtility.DepthOf(m) == home)
                ?? LayerUtility.Chain(map).LastOrDefault(m => LayerUtility.DepthOf(m) < home);
            if (homeMap == null || homeMap == map)
            {
                return null;
            }

            Job homeJob = LayerTravelUtility.EnterJob(pawn, LayerUtility.NextPortalToward(map, homeMap));
            if (homeJob != null)
            {
                comp.SetOrder(pawn, new TravelOrder { target = homeMap, kind = TravelKind.Home });
            }

            return homeJob;
        }
    }

    // 바닐라 작업 탐색이 이 층에서 고른 일과, 갱도로 이어진 다른 층에서 고를 일을 바닐라 순서로 비교한다.
    // 다른 층 일이 순서상 앞서면 (또는 이 층에 일이 없으면) 그 층으로 간다. 같은 묶음이면 가까운 이 층 일을 한다.
    [HarmonyLib.HarmonyPatch(typeof(JobGiver_Work), nameof(JobGiver_Work.TryIssueJobPackage))]
    internal static class JobGiver_Work_LayerWork_Patch
    {
        private static void Postfix(JobGiver_Work __instance, Pawn pawn, ref ThinkResult __result)
        {
            if (__instance.emergency || GhostUtility.Active || !LayerTravelUtility.CanUseLayers(pawn))
            {
                return;
            }

            GameComponent_LayerTravel comp = GameComponent_LayerTravel.Instance;
            if (comp == null || comp.GetOrder(pawn) != null)
            {
                return;
            }

            int rankHere = LayerTravelUtility.Rank(pawn, __result.Job);
            if (rankHere == 0 || !comp.ScanDue(pawn, typeof(JobGiver_Work)))
            {
                return;
            }

            // 모든 다른 층을 바닐라로 판단해 보고, 순위가 가장 앞서는 일을 고른다. 순위가 같으면 실제로 가는 거리가 짧은 층.
            Map map = pawn.Map;
            Map best = null;
            int bestRank = rankHere;
            float bestCost = float.MaxValue;
            foreach (Map other in LayerUtility.Chain(map))
            {
                if (other == map || comp.IsWasted(pawn, other))
                {
                    continue;
                }

                Job there = LayerTravelUtility.ProbeWorkOn(__instance, pawn, other);
                int rank = LayerTravelUtility.Rank(pawn, there);
                float cost = there == null ? float.MaxValue : LayerUtility.TravelCost(map, pawn.Position, other, there.targetA.Cell);
                LayerDebug.Log(pawn, "work on " + LayerUtility.LayerLabel(LayerUtility.DepthOf(other)) + ": " + (there == null ? "none" : there.def.defName + " (" + there.workGiverDef?.defName + ", rank " + rank + ", cost " + cost.ToString("0") + ")") + " vs here rank " + rankHere);
                if (there != null && (rank < bestRank || (rank == bestRank && best != null && cost < bestCost)))
                {
                    best = other;
                    bestRank = rank;
                    bestCost = cost;
                }
            }

            Job go = best == null ? null : LayerTravelUtility.EnterJob(pawn, LayerUtility.NextPortalToward(map, best));
            if (go != null)
            {
                comp.SetOrder(pawn, new TravelOrder { target = best, kind = TravelKind.Work });
                __result = new ThinkResult(go, __instance, JobTag.Misc);
            }
        }
    }

    // 층 묶음을 한 정착지로: 정착민 핵심 행동 트리(MainColonistBehaviorCore)의 욕구·행동 판단 노드(먹기·잠·오락·약 등)가
    // 이 층에서 답을 못 내거나 바닐라의 차선책(침대 없이 바닥에서 자기)을 내면, 같은 노드로 다른 층을 판단해 본다(고스트 판정).
    // 다른 층에서 제대로 된 답이 나오면 그 층으로 간다. 어떤 노드인지 하나하나 정하지 않고, 그 트리에 들어 있는 노드 종류를 그대로 쓴다.
    [HarmonyLib.HarmonyPatch(typeof(ThinkNode_JobGiver), nameof(ThinkNode_JobGiver.TryIssueJobPackage))]
    internal static class ThinkNode_JobGiver_AcrossLayers_Patch
    {
        private static HashSet<System.Type> coreTypes;

        private static HashSet<System.Type> CoreTypes
        {
            get
            {
                if (coreTypes == null)
                {
                    coreTypes = new HashSet<System.Type>();
                    ThinkTreeDef core = DefDatabase<ThinkTreeDef>.GetNamedSilentFail("MainColonistBehaviorCore");
                    if (core?.thinkRoot != null)
                    {
                        foreach (ThinkNode node in core.thinkRoot.ChildrenRecursive)
                        {
                            if (node is ThinkNode_JobGiver)
                            {
                                coreTypes.Add(node.GetType());
                            }
                        }
                    }
                }

                return coreTypes;
            }
        }

        // 바닐라가 마땅한 답이 없을 때 내는 차선책.
        private static bool IsFallback(Job job)
        {
            return job != null && job.def == JobDefOf.LayDown && !(job.targetA.Thing is Building_Bed);
        }

        private static void Postfix(ThinkNode_JobGiver __instance, Pawn pawn, JobIssueParams jobParams, ref ThinkResult __result)
        {
            if (GhostUtility.Active || (__result.IsValid && !IsFallback(__result.Job)) || !CoreTypes.Contains(__instance.GetType()) || !LayerTravelUtility.CanUseLayers(pawn))
            {
                return;
            }

            GameComponent_LayerTravel comp = GameComponent_LayerTravel.Instance;
            if (comp == null || comp.GetOrder(pawn) != null || !comp.ScanDue(pawn, __instance.GetType()))
            {
                return;
            }

            // 모든 다른 층을 같은 노드로 판단해 보고, 제대로 된 답이 나온 층 중 실제로 가는 거리가 가장 짧은 층으로 간다.
            Map map = pawn.Map;
            Map best = null;
            float bestCost = float.MaxValue;
            foreach (Map other in LayerUtility.Chain(map))
            {
                IntVec3 cell = other == map ? IntVec3.Invalid : LayerUtility.ArrivalCellOn(map, other);
                if (!cell.IsValid)
                {
                    continue;
                }

                Job there = GhostUtility.Run(pawn, other, cell, () => __instance.TryIssueJobPackage(pawn, jobParams).Job, null);
                LayerDebug.Log(pawn, "need " + __instance.GetType().Name + " on " + LayerUtility.LayerLabel(LayerUtility.DepthOf(other)) + ": " + (there?.def.defName ?? "none"));
                if (there == null || IsFallback(there))
                {
                    continue;
                }

                float cost = LayerUtility.TravelCost(map, pawn.Position, other, there.targetA.Cell);
                if (cost < bestCost)
                {
                    best = other;
                    bestCost = cost;
                }
            }

            Job go = best == null ? null : LayerTravelUtility.EnterJob(pawn, LayerUtility.NextPortalToward(map, best));
            if (go != null)
            {
                comp.SetOrder(pawn, new TravelOrder { target = best, kind = TravelKind.Need });
                __result = new ThinkResult(go, __instance, JobTag.Misc);
            }
        }
    }
}
