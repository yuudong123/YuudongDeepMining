using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace YuudongDeepMining
{
    // 층 전환 UI와 원격 선택.
    // 이 UI(또는 단축키)로 층을 바꾸면 선택했던 림이 '원격 선택'으로 남는다. 그 상태로 우클릭하면
    // 바닐라 우클릭 메뉴를 그 림이 이 층 사다리 앞에 있다고 치고 만들어 보여 주고(고스트 판정),
    // 고른 명령은 림이 사다리를 타고 이 층에 도착했을 때 같은 이름의 명령으로 실행한다.
    [StaticConstructorOnStartup]
    public static class RemoteControl
    {
        private static readonly List<Pawn> remote = new List<Pawn>();

        private static readonly Dictionary<Map, IntVec3> cameraByMap = new Dictionary<Map, IntVec3>();

        private const float PanelWidth = 150f;
        private const float RowHeight = 26f;

        public static bool HasRemote => remote.Count > 0;

        public static IReadOnlyList<Pawn> Remote => remote;

        public static void Clear()
        {
            remote.Clear();
        }

        public static void Remove(Pawn pawn)
        {
            remote.Remove(pawn);
        }

        // 층을 바꾸고, 선택했던 림은 원격 선택으로 넘기며, 이 층에 있는 원격 선택 림은 다시 선택한다.
        public static void SwitchTo(Map target)
        {
            Map current = Find.CurrentMap;
            if (target == null || target == current)
            {
                return;
            }

            foreach (Pawn pawn in Find.Selector.SelectedPawns)
            {
                if (pawn.CanTakeOrder && pawn.Map != target && !remote.Contains(pawn))
                {
                    remote.Add(pawn);
                }
            }

            if (current != null)
            {
                cameraByMap[current] = Find.CameraDriver.MapPosition;
            }

            IntVec3 focus;
            if (!cameraByMap.TryGetValue(target, out focus) || !focus.InBounds(target))
            {
                Thing portal = (Thing)LayerUtility.UpExit(target) ?? LayerUtility.DownShaft(target);
                focus = portal?.Position ?? target.Center;
            }

            CameraJumper.TryJump(focus, target, CameraJumper.MovementMode.Cut);
            foreach (Pawn pawn in remote.Where(p => p.Spawned && p.Map == target).ToList())
            {
                remote.Remove(pawn);
                Find.Selector.Select(pawn, false, false);
            }
        }

        private static void Cleanup(Map current)
        {
            remote.RemoveAll(p => p == null || p.Dead || !p.Spawned || p.Map == current || !LayerUtility.SameChain(p.Map, current));
        }

        public static void OnGUI()
        {
            Map map = Find.CurrentMap;
            if (map == null || WorldRendererUtility.WorldSelected || Find.UIRoot.screenshotMode.FiltersCurrentEvent)
            {
                return;
            }

            List<Map> chain = LayerUtility.Chain(map);
            if (chain.Count < 2)
            {
                remote.Clear();
                return;
            }

            Cleanup(map);
            int index = chain.IndexOf(map);
            if (YDMDefOf.YDM_LayerUp != null && YDMDefOf.YDM_LayerUp.KeyDownEvent && index > 0)
            {
                Event.current.Use();
                SwitchTo(chain[index - 1]);
                return;
            }

            if (YDMDefOf.YDM_LayerDown != null && YDMDefOf.YDM_LayerDown.KeyDownEvent && index < chain.Count - 1)
            {
                Event.current.Use();
                SwitchTo(chain[index + 1]);
                return;
            }

            if (remote.Count > 0 && Find.Selector.NumSelected == 0 && KeyBindingDefOf.Cancel.KeyDownEvent)
            {
                remote.Clear();
                Event.current.Use();
            }

            DrawPanel(map, chain);
        }

        private static void DrawPanel(Map map, List<Map> chain)
        {
            float height = 28f + chain.Count * RowHeight + (remote.Count > 0 ? 58f : 0f);
            // 오른쪽 아래, 날짜·속도 컨트롤(GlobalControls) 왼쪽. 오른쪽 위는 알림 칸과 겹친다.
            Rect outer = new Rect(UI.screenWidth - GlobalControls.Width - PanelWidth - 12f, UI.screenHeight - 35f - height - 8f, PanelWidth, height);
            Widgets.DrawWindowBackground(outer);
            Rect inner = outer.ContractedBy(5f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 18f), "YDM.LayerPanelTitle".Translate());
            float y = inner.y + 20f;
            foreach (Map layer in chain)
            {
                Rect row = new Rect(inner.x, y, inner.width, RowHeight - 2f);
                int colonists = layer.mapPawns.FreeColonistsSpawnedCount;
                string label = LayerUtility.LayerLabel(LayerUtility.DepthOf(layer)) + (colonists > 0 ? "  (" + colonists + ")" : "");
                if (layer == map)
                {
                    Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.12f));
                    Widgets.Label(row.ContractedBy(4f, 0f), label);
                }
                else if (Widgets.ButtonText(row, label, true, true, true, TextAnchor.MiddleLeft))
                {
                    SwitchTo(layer);
                }

                TooltipHandler.TipRegion(row, "YDM.LayerPanelTip".Translate(YDMDefOf.YDM_LayerUp?.MainKeyLabel ?? "", YDMDefOf.YDM_LayerDown?.MainKeyLabel ?? ""));
                y += RowHeight;
            }

            if (remote.Count > 0)
            {
                Widgets.Label(new Rect(inner.x, y + 2f, inner.width - 20f, 18f), "YDM.RemoteSelected".Translate(remote.Count));
                if (Widgets.ButtonImage(new Rect(inner.xMax - 16f, y + 3f, 14f, 14f), TexButton.CloseXSmall))
                {
                    remote.Clear();
                }

                float x = inner.x;
                foreach (Pawn pawn in remote.ToList())
                {
                    if (x + 26f > inner.xMax)
                    {
                        break;
                    }

                    Rect icon = new Rect(x, y + 22f, 26f, 26f);
                    Widgets.DrawBoxSolid(icon, new Color(0f, 0f, 0f, 0.25f));
                    Widgets.ThingIcon(icon, pawn);
                    TooltipHandler.TipRegion(icon, pawn.LabelCap + " (" + LayerUtility.LayerLabel(LayerUtility.DepthOf(pawn.Map)) + ")");
                    if (Widgets.ButtonInvisible(icon))
                    {
                        remote.Remove(pawn);
                    }

                    x += 28f;
                }
            }

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        // 원격 선택 림으로 우클릭 메뉴를 만든다.
        public static void OpenMenu(Vector3 clickPos)
        {
            Map map = Find.CurrentMap;
            IntVec3 cell = IntVec3.FromVector3(clickPos);
            List<Pawn> pawns = remote.Where(p => p.Spawned && p.CanTakeOrder && p.Map != map && LayerUtility.SameChain(p.Map, map)).ToList();
            if (pawns.Count == 0 || !cell.InBounds(map))
            {
                return;
            }

            List<FloatMenuOption> vanilla = GhostUtility.Run(pawns, map, p => LayerUtility.ArrivalCellOn(p.Map, map),
                () => FloatMenuMakerMap.GetOptions(pawns.ToList(), clickPos, out FloatMenuContext _), new List<FloatMenuOption>());

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            if (cell.Standable(map))
            {
                options.Add(new FloatMenuOption("YDM.RemoteMoveHere".Translate(), () => Issue(pawns, map, cell, null, true)));
            }

            HashSet<string> seen = new HashSet<string>();
            foreach (FloatMenuOption option in vanilla)
            {
                string label = option.Label;
                if (option.isGoto || label.NullOrEmpty() || !seen.Add(label))
                {
                    continue;
                }

                options.Add(option.Disabled ? new FloatMenuOption(label, null) : new FloatMenuOption(label, () => Issue(pawns, map, cell, label, false)));
            }

            if (options.Count == 0)
            {
                return;
            }

            string title = pawns.Count == 1 ? "YDM.RemoteMenuTitle".Translate(pawns[0].LabelShortCap).ToString() : "YDM.RemoteMenuTitleMulti".Translate(pawns.Count).ToString();
            Find.WindowStack.Add(new FloatMenu(options, title));
        }

        private static void Issue(List<Pawn> pawns, Map map, IntVec3 cell, string label, bool move)
        {
            GameComponent_LayerTravel comp = GameComponent_LayerTravel.Instance;
            if (comp == null)
            {
                return;
            }

            foreach (Pawn pawn in pawns)
            {
                if (pawn.Spawned && !pawn.Dead)
                {
                    comp.SetOrder(pawn, new TravelOrder { target = map, kind = TravelKind.Remote, label = label, cell = cell, move = move });
                }
            }

            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        // 림이 대상 층에 도착했다: 같은 이름의 우클릭 명령을 고르거나 그 칸으로 이동한다.
        public static void ExecuteOnArrival(Pawn pawn, TravelOrder order)
        {
            Map map = pawn.Map;
            bool done = false;
            GhostUtility.WithCurrentMap(map, delegate
            {
                if (order.move)
                {
                    IntVec3 dest = order.cell.Standable(map) ? order.cell : CellFinder.StandableCellNear(order.cell, map, 3f);
                    if (dest.IsValid)
                    {
                        Job job = JobMaker.MakeJob(JobDefOf.Goto, dest);
                        done = pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                    }

                    return;
                }

                List<FloatMenuOption> options = FloatMenuMakerMap.GetOptions(new List<Pawn> { pawn }, order.cell.ToVector3Shifted(), out FloatMenuContext _);
                FloatMenuOption match = options.FirstOrDefault(o => o.Label == order.label && !o.Disabled);
                if (match != null)
                {
                    match.Chosen(true, null);
                    done = true;
                }
            });

            if (!done)
            {
                Messages.Message("YDM.RemoteOrderFailed".Translate(pawn.LabelShort, order.label ?? "YDM.RemoteMoveHere".Translate().ToString()), pawn, MessageTypeDefOf.RejectInput, false);
            }

            remote.Remove(pawn);
            if (Find.CurrentMap == map && !Find.Selector.IsSelected(pawn))
            {
                Find.Selector.Select(pawn, false, false);
            }
        }
    }

    [HarmonyPatch(typeof(MapInterface), nameof(MapInterface.MapInterfaceOnGUI_BeforeMainTabs))]
    internal static class MapInterface_OnGUI_LayerPanel_Patch
    {
        private static void Postfix()
        {
            if (Current.ProgramState == ProgramState.Playing)
            {
                RemoteControl.OnGUI();
            }
        }
    }

    // 원격 선택이 있고 이 층에서 선택한 림이 없을 때 우클릭하면 원격 명령 메뉴를 연다.
    [HarmonyPatch(typeof(Selector), "HandleMapClicks")]
    internal static class Selector_HandleMapClicks_Remote_Patch
    {
        private static bool Prefix(Selector __instance)
        {
            if (Event.current.type != EventType.MouseDown || Event.current.button != 1 || !RemoteControl.HasRemote || __instance.SelectedPawns.Any())
            {
                return true;
            }

            RemoteControl.OpenMenu(UI.MouseMapPosition());
            Event.current.Use();
            return false;
        }
    }
}
