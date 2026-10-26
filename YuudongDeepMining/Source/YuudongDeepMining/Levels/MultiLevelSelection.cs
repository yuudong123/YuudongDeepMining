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

    // 콜로니스트 바를 드래그해 고르면, 다른 층 림도 원격 선택에 넣는다 (바닐라는 보고 있는 지도의 림만 고른다).
    [HarmonyPatch(typeof(Selector), "SelectInsideDragBox")]
    internal static class Selector_SelectInsideDragBox_Remote_Patch
    {
        private static void Postfix(Selector __instance)
        {
            Map current = Find.CurrentMap;
            if (current == null || CountAggregate.ChainOf(current) == null)
            {
                return;
            }

            if (!Selector.ShiftIsHeld)
            {
                RemoteControl.Clear();
            }

            foreach (Thing thing in Find.ColonistBar.ColonistsOrCorpsesInScreenRect(__instance.dragBox.ScreenRect))
            {
                if (thing is Pawn pawn && MultiLevelSelection.Eligible(pawn, current))
                {
                    RemoteControl.Add(pawn);
                }
            }
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
