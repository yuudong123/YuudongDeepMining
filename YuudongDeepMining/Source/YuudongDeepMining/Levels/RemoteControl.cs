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

        private static Rect panelRect;

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

        public static void Add(Pawn pawn)
        {
            if (pawn != null && !remote.Contains(pawn))
            {
                remote.Add(pawn);
            }
        }

        public static bool IsRemote(Pawn pawn)
        {
            return remote.Contains(pawn);
        }

        // 층을 바꾸고, 선택했던 림은 원격 선택으로 넘기며, 이 층에 있는 원격 선택 림은 다시 선택한다.
        public static void SwitchTo(Map target)
        {
            Map current = Find.CurrentMap;
            if (target == null || target == current)
            {
                return;
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

            // 선택한 림을 원격 선택으로 넘기고 이 층 림을 다시 선택하는 일은 지도 전환 패치가 한다 (어떤 방법으로 층을 바꿔도 같게).
            CameraJumper.TryJump(focus, target, CameraJumper.MovementMode.Cut);
        }

        // 지도가 바뀌기 직전(바닐라가 선택을 비우기 전): 같은 갱도 줄의 다른 층에 있는 선택 림을 원격 선택으로 남긴다.
        public static void CaptureSelection(Map target)
        {
            if (target == null)
            {
                return;
            }

            foreach (Pawn pawn in Find.Selector.SelectedPawns)
            {
                if (pawn.CanTakeOrder && pawn.Spawned && pawn.Map != target && LayerUtility.SameChain(pawn.Map, target) && !remote.Contains(pawn))
                {
                    remote.Add(pawn);
                }
            }
        }

        // 지도가 바뀐 직후: 이 층에 있는 원격 선택 림은 다시 일반 선택으로.
        public static void RestoreSelection(Map current)
        {
            foreach (Pawn pawn in remote.Where(p => p.Spawned && p.Map == current).ToList())
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
            // OnGUI는 이벤트마다(레이아웃·마우스 이동 등 포함) 불린다. 그리기·클릭·키 입력 때만 일한다.
            EventType type = Event.current.type;
            if (type != EventType.Repaint && type != EventType.MouseDown && type != EventType.MouseUp && type != EventType.KeyDown)
            {
                return;
            }

            using YDMProfiler.Scope __perf = YDMProfiler.Measure(YDMProfiler.Cat.LevelPanel);
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

            if (type == EventType.Repaint)
            {
                Cleanup(map);
                YDMProfiler.Update();
            }

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

            // 이 층에서 아무도 선택하지 않았으면 소집 단축키는 원격 선택 림에게 (선택이 있으면 바닐라 소집 버튼이 함께 처리한다).
            if (remote.Count > 0 && Find.Selector.NumSelected == 0 && KeyBindingDefOf.Command_ColonistDraft.KeyDownEvent)
            {
                MultiLevelSelection.ToggleRemoteDraft();
                Event.current.Use();
            }

            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && Selector.ShiftIsHeld && Find.ColonistBar.ColonistOrCorpseAt(UI.MousePositionOnUIInverted) is Pawn clicked
                && MultiLevelSelection.Eligible(clicked, map))
            {
                if (remote.Contains(clicked))
                {
                    remote.Remove(clicked);
                }
                else
                {
                    remote.Add(clicked);
                }

                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                e.Use();
                return;
            }

            if (e.type == EventType.MouseDown && LayerDebug.On)
            {
                LayerDebug.LogUI("mouse down: button " + e.button + ", remote " + remote.Count + ", selected pawns " + Find.Selector.SelectedPawns.Count
                    + ", over window " + (Find.WindowStack.GetWindowAt(UI.MousePositionOnUIInverted) != null));
            }

            DrawPanel(map, chain);

            // 우클릭 처리: 이 지점(MapInterfaceOnGUI_BeforeMainTabs)은 큰 메서드라 패치가 확실히 돈다.
            // 창보다 먼저 입력을 받으므로, 마우스가 창이나 아래 메인 버튼 줄 위에 있으면 건드리지 않는다.
            Vector2 mouse = UI.MousePositionOnUIInverted;
            if (e.type == EventType.MouseDown && e.button == 1 && Find.WindowStack.GetWindowAt(mouse) == null && mouse.y < UI.screenHeight - 35f && !panelRect.Contains(mouse))
            {
                RemoteRightClick.Handle("panel OnGUI");
            }
        }

        // 층 버튼 글자·도움말은 1초에 한 번(또는 갱도 줄이 바뀔 때)만 만든다.
        private static List<Map> labelChain;
        private static string[] labels = new string[0];
        private static string title;
        private static string tip;
        private static long labelsMs;

        private static void RefreshLabels(List<Map> chain)
        {
            long now = System.Environment.TickCount64;
            if (chain == labelChain && labels.Length == chain.Count && now - labelsMs < 1000)
            {
                return;
            }

            labelChain = chain;
            labelsMs = now;
            labels = new string[chain.Count];
            for (int i = 0; i < chain.Count; i++)
            {
                int colonists = chain[i].mapPawns.FreeColonistsSpawnedCount;
                labels[i] = LayerUtility.LayerLabel(LayerUtility.DepthOf(chain[i])) + (colonists > 0 ? "  (" + colonists + ")" : "");
            }

            title = "YDM.LayerPanelTitle".Translate();
            tip = "YDM.LayerPanelTip".Translate(YDMDefOf.YDM_LayerUp?.MainKeyLabel ?? "", YDMDefOf.YDM_LayerDown?.MainKeyLabel ?? "");
        }

        private static void DrawPanel(Map map, List<Map> chain)
        {
            RefreshLabels(chain);
            float height = 28f + chain.Count * RowHeight + (remote.Count > 0 ? 58f : 0f);
            // 오른쪽 아래, 날짜·속도 컨트롤(GlobalControls) 왼쪽. 오른쪽 위는 알림 칸과 겹친다.
            Rect outer = new Rect(UI.screenWidth - GlobalControls.Width - PanelWidth - 12f, UI.screenHeight - 35f - height - 8f, PanelWidth, height);
            panelRect = outer;
            YDMProfiler.Draw(outer);
            Widgets.DrawWindowBackground(outer);
            Rect inner = outer.ContractedBy(5f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 18f), title);
            float y = inner.y + 20f;
            for (int i = 0; i < chain.Count; i++)
            {
                Map layer = chain[i];
                Rect row = new Rect(inner.x, y, inner.width, RowHeight - 2f);
                string label = labels[i];
                if (layer == map)
                {
                    Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.12f));
                    Widgets.Label(row.ContractedBy(4f, 0f), label);
                }
                else if (Widgets.ButtonText(row, label, true, true, true, TextAnchor.MiddleLeft))
                {
                    SwitchTo(layer);
                }

                TooltipHandler.TipRegion(row, tip);
                y += RowHeight;
            }

            if (remote.Count > 0)
            {
                Widgets.Label(new Rect(inner.x, y + 2f, inner.width - 64f, 18f), "YDM.RemoteSelected".Translate(remote.Count));
                if (Widgets.ButtonText(new Rect(inner.xMax - 62f, y + 1f, 44f, 18f), "YDM.RemoteDraft".Translate()))
                {
                    MultiLevelSelection.ToggleRemoteDraft();
                }

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
                    Pawn tipPawn = pawn;
                    TooltipHandler.TipRegion(icon, () => tipPawn.LabelCap + " (" + LayerUtility.LayerLabel(LayerUtility.DepthOf(tipPawn.Map)) + ")", tipPawn.thingIDNumber ^ 0x59444D);
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
            OpenMenu(clickPos, new List<Pawn>());
        }

        // local: 이 층에서 바닐라로 선택한 림. 원격 선택 림과 함께 한 메뉴로 명령한다.
        public static void OpenMenu(Vector3 clickPos, List<Pawn> local)
        {
            Map map = Find.CurrentMap;
            IntVec3 cell = IntVec3.FromVector3(clickPos);
            List<Pawn> pawns = remote.Where(p => p.Spawned && p.CanTakeOrder && p.Map != map && LayerUtility.SameChain(p.Map, map)).ToList();
            if (LayerDebug.On)
            {
                LayerDebug.LogUI("right-click at " + cell + ": remote " + remote.Count + ", usable " + pawns.Count + ", local " + local.Count);
            }

            if (pawns.Count == 0 || !cell.InBounds(map))
            {
                Messages.Message("YDM.RemoteNoOptions".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            List<Pawn> everyone = local.Concat(pawns).ToList();
            List<FloatMenuOption> vanilla = GhostUtility.Run(pawns, map, p => LayerUtility.ArrivalCellOn(p.Map, map),
                () => FloatMenuMakerMap.GetOptions(everyone.ToList(), clickPos, out FloatMenuContext _), new List<FloatMenuOption>());

            if (LayerDebug.On)
            {
                LayerDebug.Log(pawns[0], "remote menu at " + cell + ": " + vanilla.Count + " vanilla options");
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            IntVec3 moveCell = cell.Standable(map) ? cell : CellFinder.StandableCellNear(cell, map, 3f);
            if (moveCell.IsValid)
            {
                options.Add(new FloatMenuOption("YDM.RemoteMoveHere".Translate(), delegate
                {
                    IssueLocal(local, clickPos, moveCell, null, true);
                    Issue(pawns, map, moveCell, null, true);
                }));
            }

            HashSet<string> seen = new HashSet<string>();
            foreach (FloatMenuOption option in vanilla)
            {
                string label = option.Label;
                if (option.isGoto || label.NullOrEmpty() || !seen.Add(label))
                {
                    continue;
                }

                Thing focus = option.revalidateClickTarget ?? cell.GetFirstPawn(map);
                options.Add(option.Disabled ? new FloatMenuOption(label, null) : new FloatMenuOption(label, delegate
                {
                    IssueLocal(local, clickPos, cell, label, false);
                    Issue(pawns, map, cell, label, false, focus);
                }));
            }

            if (options.Count == 0)
            {
                Messages.Message("YDM.RemoteNoOptions".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            string title = everyone.Count == 1 ? "YDM.RemoteMenuTitle".Translate(pawns[0].LabelShortCap).ToString() : "YDM.RemoteMenuTitleMulti".Translate(everyone.Count).ToString();
            Find.WindowStack.Add(new FloatMenu(options, title));
        }

        private static void IssueLocal(List<Pawn> local, Vector3 clickPos, IntVec3 cell, string label, bool move)
        {
            if (local.Count == 0)
            {
                return;
            }

            if (move)
            {
                foreach (Pawn pawn in local)
                {
                    pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto, cell), JobTag.Misc);
                }

                return;
            }

            FloatMenuOption together = FloatMenuMakerMap.GetOptions(local.ToList(), clickPos, out FloatMenuContext _).FirstOrDefault(o => o.Label == label && !o.Disabled);
            if (together != null)
            {
                together.Chosen(true, null);
                return;
            }

            foreach (Pawn pawn in local)
            {
                FloatMenuMakerMap.GetOptions(new List<Pawn> { pawn }, clickPos, out FloatMenuContext _).FirstOrDefault(o => o.Label == label && !o.Disabled)?.Chosen(true, null);
            }
        }

        private static void Issue(List<Pawn> pawns, Map map, IntVec3 cell, string label, bool move, Thing focus = null)
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
                    comp.SetOrder(pawn, new TravelOrder { target = map, kind = TravelKind.Remote, label = label, cell = cell, move = move, focus = focus });
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

                Vector3 clickPos = order.cell.ToVector3Shifted();
                if (order.focus != null && order.focus.Spawned && order.focus.Map == map)
                {
                    clickPos = order.focus is Pawn ? order.focus.DrawPos : order.focus.Position.ToVector3Shifted();
                }

                List<FloatMenuOption> options = FloatMenuMakerMap.GetOptions(new List<Pawn> { pawn }, clickPos, out FloatMenuContext _);
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
    // 원본은 막지 않고 이벤트만 소비한다 (소비된 우클릭은 바닐라가 보지 않는다).
    // .NET 10에서는 작은 메서드가 호출부에 인라인되면 그 메서드의 패치가 무시될 수 있어서, 바닐라 입력 흐름의 두 지점에 같이 건다.
    // 먼저 도는 쪽이 이벤트를 소비하면 다른 쪽은 그냥 지나간다.
    internal static class RemoteRightClick
    {
        public static void Handle(string source)
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.MouseDown || e.button != 1)
            {
                return;
            }

            if (LayerDebug.On)
            {
                LayerDebug.LogUI("right-click reached " + source + ": remote " + RemoteControl.Remote.Count);
            }

            if (!RemoteControl.HasRemote || Find.CurrentMap == null)
            {
                return;
            }

            List<Pawn> local = Find.Selector.SelectedPawns.Where(p => p.CanTakeOrder && p.Map == Find.CurrentMap).ToList();
            if (local.Count > 0 && !RemoteControl.Remote.Any(p => MultiLevelSelection.Eligible(p, Find.CurrentMap)))
            {
                if (LayerDebug.On)
                {
                    LayerDebug.LogUI("right-click: no usable remote pawns, vanilla menu");
                }

                return;
            }

            RemoteControl.OpenMenu(UI.MouseMapPosition(), local);
            e.Use();
        }
    }

    [HarmonyPatch(typeof(MapInterface), nameof(MapInterface.HandleLowPriorityInput))]
    internal static class MapInterface_HandleLowPriorityInput_Remote_Patch
    {
        private static void Prefix()
        {
            RemoteRightClick.Handle("HandleLowPriorityInput");
        }
    }

    [HarmonyPatch(typeof(Selector), nameof(Selector.SelectorOnGUI))]
    internal static class Selector_SelectorOnGUI_Remote_Patch
    {
        private static void Prefix()
        {
            RemoteRightClick.Handle("SelectorOnGUI");
        }
    }

    // 어떤 방법으로 층을 바꾸든(층 패널, 콜로니스트 바, 사다리의 '지상 보기' 등) 선택한 림이 원격 선택으로 남게 한다.
    // 바닐라 지도 전환 처리는 그대로 두고, 앞에서 선택을 기록하고 뒤에서 이 층 림을 다시 선택한다.
    [HarmonyPatch(typeof(MapInterface), nameof(MapInterface.Notify_SwitchedMap))]
    internal static class MapInterface_SwitchedMap_Remote_Patch
    {
        private static void Prefix()
        {
            if (Current.ProgramState == ProgramState.Playing)
            {
                RemoteControl.CaptureSelection(Find.CurrentMap);
            }
        }

        private static void Postfix()
        {
            if (Current.ProgramState == ProgramState.Playing && Find.CurrentMap != null)
            {
                RemoteControl.RestoreSelection(Find.CurrentMap);
            }
        }
    }
}
