using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace YuudongDeepMining
{
    // 깊을수록 바위·광석이 받는 피해(채굴 포함)가 줄어든다.
    [HarmonyPatch(typeof(Mineable), nameof(Mineable.PreApplyDamage))]
    internal static class Mineable_PreApplyDamage_Patch
    {
        private static void Prefix(Mineable __instance, ref DamageInfo dinfo)
        {
            Map map = __instance.Map;
            if (map == null)
            {
                return;
            }

            int depth = LayerUtility.DepthOf(map);
            if (depth <= 0)
            {
                return;
            }

            float hardness = LayerUtility.HardnessAt(depth);
            if (hardness > 1f)
            {
                dinfo.SetAmount(dinfo.Amount / hardness);
            }
        }
    }

    // 갱도를 노리지 않은 탄은 갱도에 맞지 않고 지나간다 (빗나간 탄, 엄폐물로 걸린 탄 등).
    [HarmonyPatch(typeof(Projectile), "CanHit")]
    internal static class Projectile_CanHit_Patch
    {
        private static bool Prefix(Thing thing, LocalTargetInfo ___intendedTarget, ref bool __result)
        {
            if (thing is Building_MineShaft && ___intendedTarget.Thing != thing)
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    // 정착민에게 '작업 층' 버튼을 붙인다 (지하 층이 하나라도 있을 때).
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    internal static class Pawn_GetGizmos_Patch
    {
        private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> gizmos, Pawn __instance)
        {
            foreach (Gizmo gizmo in gizmos)
            {
                yield return gizmo;
            }

            if (__instance.IsColonistPlayerControlled && __instance.Spawned && LayerUtility.AnyLayerExists())
            {
                yield return LayerAssignmentUtility.MakeGizmo(__instance);
            }
        }
    }
}
