using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace YuudongDeepMining
{
    // 정착민별 '작업 층'. -1이면 자유(지금 층에서 지냄), 0이면 지상, n이면 지하 n층.
    public class GameComponent_LayerAssignment : GameComponent
    {
        public const int Free = -1;

        private Dictionary<Pawn, int> assigned = new Dictionary<Pawn, int>();

        private List<Pawn> tmpKeys;

        private List<int> tmpValues;

        public GameComponent_LayerAssignment(Game game)
        {
        }

        public static GameComponent_LayerAssignment Instance => Current.Game?.GetComponent<GameComponent_LayerAssignment>();

        public int Get(Pawn pawn)
        {
            return pawn != null && assigned.TryGetValue(pawn, out int depth) ? depth : Free;
        }

        public void Set(Pawn pawn, int depth)
        {
            if (depth == Free)
            {
                assigned.Remove(pawn);
            }
            else
            {
                assigned[pawn] = depth;
            }
        }

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                assigned.RemoveAll(kv => kv.Key == null || kv.Key.Dead || kv.Key.Destroyed);
            }

            Scribe_Collections.Look(ref assigned, "assigned", LookMode.Reference, LookMode.Value, ref tmpKeys, ref tmpValues);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                assigned ??= new Dictionary<Pawn, int>();
                assigned.RemoveAll(kv => kv.Key == null);
            }
        }
    }

    [StaticConstructorOnStartup]
    public static class LayerAssignmentUtility
    {
        private static readonly Texture2D Icon = ContentFinder<Texture2D>.Get("UI/Commands/EnterCave");

        public static string AssignmentLabel(int depth)
        {
            return depth == GameComponent_LayerAssignment.Free ? "YDM.AssignFree".Translate().ToString() : LayerUtility.LayerLabel(depth);
        }

        public static Gizmo MakeGizmo(Pawn pawn)
        {
            GameComponent_LayerAssignment comp = GameComponent_LayerAssignment.Instance;
            int current = comp?.Get(pawn) ?? GameComponent_LayerAssignment.Free;
            return new Command_Action
            {
                defaultLabel = "YDM.AssignLabel".Translate(AssignmentLabel(current)),
                defaultDesc = "YDM.AssignDesc".Translate(),
                icon = Icon,
                groupKey = 77310421,
                action = delegate
                {
                    List<FloatMenuOption> options = new List<FloatMenuOption>();
                    int deepest = LayerUtility.DeepestLayerInGame();
                    for (int depth = GameComponent_LayerAssignment.Free; depth <= deepest; depth++)
                    {
                        int chosen = depth;
                        options.Add(new FloatMenuOption(AssignmentLabel(chosen), delegate
                        {
                            foreach (Pawn selected in Find.Selector.SelectedPawns.Where(p => p.IsColonistPlayerControlled).DefaultIfEmpty(pawn))
                            {
                                comp?.Set(selected, chosen);
                            }
                        }));
                    }

                    Find.WindowStack.Add(new FloatMenu(options));
                },
            };
        }
    }

    // 생각 트리(Humanlike_PreMain)에 끼워 넣는 노드: 필요하면 계단으로 층을 옮긴다.
    //  1) 지하에서 배고프거나 졸린데 이 층에 먹을 것·침대가 없으면 한 층 올라간다.
    //  2) 배고프거나 졸리지 않고 작업 층이 지금 층과 다르면 그쪽으로 한 층씩 옮긴다.
    public class JobGiver_TravelLayer : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            Map map = pawn.Map;
            if (map == null || pawn.Drafted || pawn.Downed || !pawn.IsColonistPlayerControlled || pawn.InMentalState)
            {
                return null;
            }

            int here = LayerUtility.DepthOf(map);
            bool isLayer = here > 0;
            Building_MineShaft down = LayerUtility.DownShaft(map);
            if (!isLayer && down == null)
            {
                return null;
            }

            if (isLayer && NeedsToGoUp(pawn, map))
            {
                return GoThrough(pawn, LayerUtility.UpExit(map));
            }

            int target = GameComponent_LayerAssignment.Instance?.Get(pawn) ?? GameComponent_LayerAssignment.Free;
            if (target == GameComponent_LayerAssignment.Free || target == here || HasUrgentNeed(pawn))
            {
                return null;
            }

            if (target < here)
            {
                return GoThrough(pawn, LayerUtility.UpExit(map));
            }

            // 아직 파지 않은 층으로는 내려가지 않는다 (작업 층 목록은 있는 층만 보여 주지만, 층이 무너졌을 수 있다).
            if (down == null || !down.PocketMapExists)
            {
                return null;
            }

            return GoThrough(pawn, down);
        }

        private static Job GoThrough(Pawn pawn, MapPortal portal)
        {
            if (portal == null || !portal.Spawned || !portal.IsEnterable(out string _) || !pawn.CanReach(portal, PathEndMode.Touch, Danger.Some))
            {
                return null;
            }

            Job job = JobMaker.MakeJob(JobDefOf.EnterPortal, portal);
            job.locomotionUrgency = LocomotionUrgency.Jog;
            return job;
        }

        // 배고프거나 졸리면 지금 층에서 먼저 해결한다 (NeedsToGoUp과 같은 기준이라 층 사이를 오가며 헤매지 않는다).
        private static bool HasUrgentNeed(Pawn pawn)
        {
            Need_Food food = pawn.needs?.food;
            if (food != null && food.CurCategory >= HungerCategory.Hungry)
            {
                return true;
            }

            Need_Rest rest = pawn.needs?.rest;
            return rest != null && rest.CurCategory >= RestCategory.Tired;
        }

        private static bool NeedsToGoUp(Pawn pawn, Map map)
        {
            Need_Food food = pawn.needs?.food;
            if (food != null && food.CurCategory >= HungerCategory.Hungry
                && !FoodUtility.TryFindBestFoodSourceFor(pawn, pawn, food.CurCategory == HungerCategory.Starving, out Thing _, out ThingDef _, allowCorpse: false))
            {
                return true;
            }

            Need_Rest rest = pawn.needs?.rest;
            if (rest != null && rest.CurCategory >= RestCategory.Tired && RestUtility.FindBedFor(pawn) == null)
            {
                return true;
            }

            return false;
        }
    }
}
