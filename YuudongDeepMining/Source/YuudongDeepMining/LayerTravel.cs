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
    }

    public class TravelOrder : IExposable
    {
        public Map target;
        public TravelKind kind;

        // 원격 명령: 도착하면 이 칸에서 같은 이름의 우클릭 명령을 고른다. move면 그 칸으로 이동만 한다.
        public string label;
        public IntVec3 cell = IntVec3.Invalid;
        public bool move;

        public void ExposeData()
        {
            Scribe_References.Look(ref target, "target");
            Scribe_Values.Look(ref kind, "kind");
            Scribe_Values.Look(ref label, "label");
            Scribe_Values.Look(ref cell, "cell", IntVec3.Invalid);
            Scribe_Values.Look(ref move, "move", false);
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
        private readonly Dictionary<Pawn, int> lastScan = new Dictionary<Pawn, int>();
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

        public bool ScanDue(Pawn pawn)
        {
            int now = Find.TickManager.TicksGame;
            if (lastScan.TryGetValue(pawn, out int last) && now - last < ScanInterval)
            {
                return false;
            }

            lastScan[pawn] = now;
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

        // 지하에서 배고프거나 졸린데 이 층에 먹을 것·침대가 없다.
        public static bool NeedsToGoUp(Pawn pawn, Map map)
        {
            Need_Food food = pawn.needs?.food;
            if (food != null && food.CurCategory >= HungerCategory.Hungry
                && !FoodUtility.TryFindBestFoodSourceFor(pawn, pawn, food.CurCategory == HungerCategory.Starving, out Thing _, out ThingDef _, allowCorpse: false))
            {
                return true;
            }

            Need_Rest rest = pawn.needs?.rest;
            return rest != null && rest.CurCategory >= RestCategory.Tired && RestUtility.FindBedFor(pawn) == null;
        }

        // 바닐라 작업 탐색을 다른 층 사다리 앞에서 해 본다 (고스트 판정).
        private static readonly JobGiver_Work ProbeWork = new JobGiver_Work();

        public static bool HasWorkOn(Pawn pawn, Map target)
        {
            IntVec3 cell = LayerUtility.ArrivalCellOn(pawn.Map, target);
            if (!cell.IsValid)
            {
                return false;
            }

            return GhostUtility.Run(pawn, target, cell, () => ProbeWork.TryIssueJobPackage(pawn, default(JobIssueParams)).Job != null, false);
        }
    }

    // 생각 트리 앞쪽(Humanlike_PreMain, 일·욕구보다 먼저):
    //  1) 지하에서 배고프거나 졸린데 이 층에 먹을 것·침대가 없으면 한 층 올라간다.
    //  2) 진행 중인 층 이동(일하러 가기·집으로·운반)을 한 층씩 이어 간다.
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

            if (LayerUtility.IsLayer(map) && LayerTravelUtility.NeedsToGoUp(pawn, map))
            {
                comp?.ClearOrder(pawn);
                return LayerTravelUtility.EnterJob(pawn, LayerUtility.UpExit(map));
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

            if (LayerTravelUtility.HasUrgentNeed(pawn))
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

    // 생각 트리 뒤쪽(Humanlike_PostMain, 이 층에 할 일이 없을 때):
    //  1) 갱도로 이어진 다른 층에 할 일이 있으면 그 층으로 간다 (가까운 층부터, 바닐라 작업 탐색 그대로).
    //  2) 없으면 주로 머무는 층으로 돌아간다.
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
            if (comp.ScanDue(pawn))
            {
                foreach (Map other in LayerUtility.Chain(map).Where(m => m != map).OrderBy(m => Mathf.Abs(LayerUtility.DepthOf(m) - here)))
                {
                    if (comp.IsWasted(pawn, other) || !LayerTravelUtility.HasWorkOn(pawn, other))
                    {
                        continue;
                    }

                    Job job = LayerTravelUtility.EnterJob(pawn, LayerUtility.NextPortalToward(map, other));
                    if (job != null)
                    {
                        comp.SetOrder(pawn, new TravelOrder { target = other, kind = TravelKind.Work });
                        return job;
                    }
                }
            }

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
}
