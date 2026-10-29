using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace YuudongDeepMining
{
    // 소유자 지정(침대·관·왕좌·명상 자리 등): 바닐라 후보는 그 건물이 있는 지도의 림뿐이다.
    // 갱도로 이어진 다른 층에서도 같은 후보 판정을 돌려(고스트 판정: 건물을 그 층에 있는 것처럼) 그 층 림을 후보에 더한다.
    // 정렬은 소유자 지정 창이 다시 하므로 이 층 후보 뒤에 다른 층 후보를 이어 붙인다.
    [HarmonyPatch]
    internal static class CompAssignableToPawn_AssigningCandidates_Patch
    {
        // 창과 버튼이 매 프레임 후보를 묻는다. 같은 프레임의 같은 건물은 한 번만 계산한다.
        private static CompAssignableToPawn cachedComp;
        private static int cachedFrame = -1;
        private static List<Pawn> cached;

        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (Type type in typeof(CompAssignableToPawn).AllSubclasses().Concat(typeof(CompAssignableToPawn)))
            {
                MethodInfo getter = AccessTools.DeclaredPropertyGetter(type, nameof(CompAssignableToPawn.AssigningCandidates));
                if (getter != null && !getter.IsAbstract)
                {
                    yield return getter;
                }
            }
        }

        private static void Postfix(CompAssignableToPawn __instance, ref IEnumerable<Pawn> __result)
        {
            Thing parent = __instance.parent;
            if (GhostUtility.Active || parent == null || !parent.Spawned)
            {
                return;
            }

            List<Map> chain = LayerUtility.Chain(parent.Map);
            if (chain.Count < 2)
            {
                return;
            }

            if (cachedComp == __instance && cachedFrame == Time.frameCount && cached != null)
            {
                __result = cached;
                return;
            }

            List<Pawn> all = __result.ToList();
            foreach (Map other in chain)
            {
                if (other == parent.Map)
                {
                    continue;
                }

                IntVec3 cell = parent.Position.InBounds(other) ? parent.Position : other.Center;
                List<Pawn> there = GhostUtility.RunThings(new Thing[] { parent }, other, _ => cell, () => __instance.AssigningCandidates.ToList(), null);
                if (there != null)
                {
                    all.AddRange(there.Where(p => !all.Contains(p)));
                }
            }

            cachedComp = __instance;
            cachedFrame = Time.frameCount;
            cached = all;
            __result = all;
        }
    }
}
