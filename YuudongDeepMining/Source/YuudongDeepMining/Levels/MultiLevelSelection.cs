using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace YuudongDeepMining
{
    // 여러 층 선택: 원격 선택(다른 층에서 선택된 림)을 콜로니스트 바에 바닐라처럼 표시하고,
    // 콜로니스트 바 드래그 선택에 다른 층 림도 넣으며, 소집 버튼을 누르면 원격 선택 림도 같이 소집한다.
    public static class MultiLevelSelection
    {
        public static bool Eligible(Pawn pawn, Map current)
        {
            return pawn != null && pawn.Spawned && !pawn.Dead && pawn.CanTakeOrder && current != null && pawn.Map != current && LayerUtility.SameChain(pawn.Map, current);
        }

        // 원격 선택 림 전체를 소집하거나 해제한다 (하나라도 소집 안 됐으면 전부 소집).
        public static void ToggleRemoteDraft()
        {
            bool draft = false;
            foreach (Pawn pawn in RemoteControl.Remote)
            {
                if (pawn.drafter != null && pawn.drafter.ShowDraftGizmo && !pawn.Drafted)
                {
                    draft = true;
                    break;
                }
            }

            SetRemoteDraft(draft);
        }

        public static void SetRemoteDraft(bool draft)
        {
            foreach (Pawn pawn in RemoteControl.Remote)
            {
                if (pawn.drafter != null && pawn.drafter.ShowDraftGizmo && pawn.Drafted != draft)
                {
                    pawn.drafter.Drafted = draft;
                }
            }
        }
    }

    // 콜로니스트 바: 원격 선택 림에도 바닐라 선택 괄호를 그린다.
    [HarmonyPatch(typeof(ColonistBarColonistDrawer), nameof(ColonistBarColonistDrawer.DrawColonist))]
    internal static class ColonistBarColonistDrawer_DrawColonist_Remote_Patch
    {
        private static void Postfix(Rect rect, Pawn colonist)
        {
            if (Event.current.type != EventType.Repaint || !RemoteControl.HasRemote || WorldRendererUtility.WorldSelected)
            {
                return;
            }

            if (RemoteControl.IsRemote(colonist))
            {
                float scale = Find.ColonistBar.Scale;
                SelectionDrawerUtility.DrawSelectionOverlayOnGUI(colonist, rect.ContractedBy(-2f * scale), 0.4f * scale, 20f * scale);
            }
        }
    }

    // 콜로니스트 바를 드래그해 여러 층 림을 고르면, 보던 층은 그대로 두고 이 층 림은 선택, 다른 층 림은 원격 선택에 넣는다.
    // 바닐라는 범위에 보던 지도 림이 있으면 그 림만 고르고, 다른 지도 림만 있으면 그 지도로 넘어간다.
    // 바닐라 선택은 그대로 돌리고, 끝난 뒤 같은 갱도 줄 안에서 넘어갔으면 보던 층·카메라로 되돌려 범위 안 림을 다시 고른다.
    [HarmonyPatch(typeof(Selector), "SelectInsideDragBox")]
    internal static class Selector_SelectInsideDragBox_Remote_Patch
    {
        internal class DragStart
        {
            public Map map;
            public IntVec3 camera;
            public readonly List<object> kept = new List<object>();
        }

        private static void Prefix(Selector __instance, out DragStart __state)
        {
            __state = new DragStart { map = Find.CurrentMap };
            if (__state.map == null)
            {
                return;
            }

            __state.camera = Find.CameraDriver.MapPosition;
            if (Selector.ShiftIsHeld)
            {
                __state.kept.AddRange(__instance.SelectedObjects);
            }
        }

        private static void Postfix(Selector __instance, DragStart __state)
        {
            Map current = Find.CurrentMap;
            if (current == null || CountAggregate.ChainOf(current) == null)
            {
                return;
            }

            List<Pawn> inBox = PawnsInRect(__instance.dragBox.ScreenRect);
            Map start = __state?.map;

            // 바닐라는 범위에 다른 지도 림만 있으면 그 지도로 넘어간다. 같은 갱도 줄 안이면 보던 층으로 돌아온다.
            bool switched = start != null && start != current && !start.Disposed && LayerUtility.SameChain(start, current);
            if (switched)
            {
                // 되돌릴 때 지도 전환 패치가 지금 선택(다른 층 림)을 원격 선택으로 넘긴다. 아래에서 드래그 범위 기준으로 다시 고른다.
                Current.Game.CurrentMap = start;
                Find.CameraDriver.JumpToCurrentMapLoc(__state.camera);
                current = start;
                foreach (object obj in __state.kept)
                {
                    if (obj is Thing kept && !kept.Destroyed && kept.MapHeld == start && !__instance.IsSelected(kept))
                    {
                        __instance.Select(kept, false, false);
                    }
                }
            }

            if (!Selector.ShiftIsHeld)
            {
                RemoteControl.Clear();
            }

            foreach (Pawn pawn in inBox)
            {
                if (switched && pawn.Spawned && !pawn.Dead && pawn.Map == current)
                {
                    if (!__instance.IsSelected(pawn))
                    {
                        __instance.Select(pawn, false, false);
                    }
                }
                else if (MultiLevelSelection.Eligible(pawn, current))
                {
                    RemoteControl.Add(pawn);
                }
            }
        }

        // 바닐라 ColonistsOrCorpsesInScreenRect는 보고 있는 지도 림이 하나라도 범위에 있으면 다른 지도 림을 빼 버린다.
        // 그래서 콜로니스트 바 칸 위치로 범위 안 림을 직접 고른다 (바닐라와 같은 계산).
        private static readonly System.Func<ColonistBar, bool> BarVisible =
            AccessTools.MethodDelegate<System.Func<ColonistBar, bool>>(AccessTools.PropertyGetter(typeof(ColonistBar), "Visible"));

        private static List<Pawn> PawnsInRect(Rect rect)
        {
            List<Pawn> result = new List<Pawn>();
            ColonistBar bar = Find.ColonistBar;
            if (!BarVisible(bar))
            {
                return result;
            }

            List<Vector2> drawLocs = bar.DrawLocs;
            List<ColonistBar.Entry> entries = bar.Entries;
            Vector2 size = bar.Size;
            for (int i = 0; i < drawLocs.Count && i < entries.Count; i++)
            {
                Pawn pawn = entries[i].pawn;
                if (pawn != null && rect.Overlaps(new Rect(drawLocs[i].x, drawLocs[i].y, size.x, size.y)))
                {
                    result.Add(pawn);
                }
            }

            return result;
        }
    }

    // 바닐라 소집 버튼(단축키 포함): 누르면 원격 선택 림도 같은 상태로 소집·해제한다.
    [HarmonyPatch(typeof(Pawn_DraftController), "GetGizmos")]
    internal static class Pawn_DraftController_GetGizmos_Remote_Patch
    {
        private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> gizmos, Pawn_DraftController __instance)
        {
            foreach (Gizmo gizmo in gizmos)
            {
                if (gizmo is Command_Toggle toggle && toggle.hotKey == KeyBindingDefOf.Command_ColonistDraft && RemoteControl.HasRemote)
                {
                    System.Action original = toggle.toggleAction;
                    Pawn pawn = __instance.pawn;
                    toggle.toggleAction = delegate
                    {
                        original?.Invoke();
                        MultiLevelSelection.SetRemoteDraft(pawn.Drafted);
                    };
                }

                yield return gizmo;
            }
        }
    }
}
