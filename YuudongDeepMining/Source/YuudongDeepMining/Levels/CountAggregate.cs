using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace YuudongDeepMining
{
    // 층 묶음을 한 정착지로 (수치 집계): 자원 수, 작업 지시서의 '~개가 될 때까지' 개수, 정착지 부(富)를 갱도 줄 전체로 센다.
    // 묶음의 어느 층에서 물어도 묶음 전체 값을 낸다. 층 지도는 '플레이어 거점'이 아니라서,
    // 거점 지도들을 더하는 바닐라 코드(식량 부족 알림, 이야기꾼 부 계산)는 맨 위 지도 하나로만 세어 두 번 세지 않는다.
    // 바닐라 계산은 그대로 두고, 같은 계산을 묶음의 다른 층에서도 돌려 더한다 (다른 층 값을 셀 때는 합산을 끄고 원래 값만).
    public static class CountAggregate
    {
        [ThreadStatic]
        private static bool computing;

        public static bool Computing => computing;

        public static List<Map> ChainOf(Map map)
        {
            if (map == null || GhostUtility.Active)
            {
                return null;
            }

            List<Map> chain = LayerUtility.Chain(map);
            return chain.Count < 2 ? null : chain;
        }

        public static T Raw<T>(Func<T> func)
        {
            bool old = computing;
            computing = true;
            try
            {
                return func();
            }
            finally
            {
                computing = old;
            }
        }
    }

    // 자원 수: 사전을 직접 읽는 함수만 묶음 합산한다 (GetCountIn(카테고리)는 GetCount를 다시 부르므로 자동으로 따라온다).
    [HarmonyPatch]
    internal static class ResourceCounter_Aggregate_Patch
    {
        private static readonly AccessTools.FieldRef<ResourceCounter, Map> MapRef = AccessTools.FieldRefAccess<ResourceCounter, Map>("map");

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ResourceCounter), nameof(ResourceCounter.GetCount), new[] { typeof(ThingDef) });
            yield return AccessTools.Method(typeof(ResourceCounter), nameof(ResourceCounter.GetCountIn), new[] { typeof(ThingRequestGroup) });
        }

        private static void Postfix(ResourceCounter __instance, MethodBase __originalMethod, object[] __args, ref int __result)
        {
            using YDMProfiler.Scope __perf = YDMProfiler.Measure(YDMProfiler.Cat.CountResources);
            if (CountAggregate.Computing)
            {
                return;
            }

            List<Map> chain = CountAggregate.ChainOf(MapRef(__instance));
            if (chain == null)
            {
                return;
            }

            Map mine = MapRef(__instance);
            int total = __result;
            foreach (Map other in chain)
            {
                if (other != mine)
                {
                    total += CountAggregate.Raw(() => (int)__originalMethod.Invoke(other.resourceCounter, __args));
                }
            }

            __result = total;
        }
    }

    [HarmonyPatch]
    internal static class ResourceCounter_AggregateFloat_Patch
    {
        private static readonly AccessTools.FieldRef<ResourceCounter, Map> MapRef = AccessTools.FieldRefAccess<ResourceCounter, Map>("map");

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.PropertyGetter(typeof(ResourceCounter), nameof(ResourceCounter.TotalHumanEdibleNutrition));
            yield return AccessTools.PropertyGetter(typeof(ResourceCounter), nameof(ResourceCounter.TotalHumanBabyEdibleNutrition));
        }

        private static void Postfix(ResourceCounter __instance, MethodBase __originalMethod, ref float __result)
        {
            using YDMProfiler.Scope __perf = YDMProfiler.Measure(YDMProfiler.Cat.CountResources);
            if (CountAggregate.Computing)
            {
                return;
            }

            Map mine = MapRef(__instance);
            List<Map> chain = CountAggregate.ChainOf(mine);
            if (chain == null)
            {
                return;
            }

            float total = __result;
            foreach (Map other in chain)
            {
                if (other != mine)
                {
                    total += CountAggregate.Raw(() => (float)__originalMethod.Invoke(other.resourceCounter, null));
                }
            }

            __result = total;
        }
    }

    // 왼쪽 위 자원 표시 등이 쓰는 전체 목록: 묶음 전체를 더한 새 사전을 돌려준다 (원래 사전은 건드리지 않는다).
    [HarmonyPatch(typeof(ResourceCounter), nameof(ResourceCounter.AllCountedAmounts), MethodType.Getter)]
    internal static class ResourceCounter_AllCounted_Aggregate_Patch
    {
        private static readonly AccessTools.FieldRef<ResourceCounter, Map> MapRef = AccessTools.FieldRefAccess<ResourceCounter, Map>("map");

        private static void Postfix(ResourceCounter __instance, ref Dictionary<ThingDef, int> __result)
        {
            using YDMProfiler.Scope __perf = YDMProfiler.Measure(YDMProfiler.Cat.CountResources);
            if (CountAggregate.Computing)
            {
                return;
            }

            Map mine = MapRef(__instance);
            List<Map> chain = CountAggregate.ChainOf(mine);
            if (chain == null)
            {
                return;
            }

            Dictionary<ThingDef, int> merged = new Dictionary<ThingDef, int>(__result);
            foreach (Map other in chain)
            {
                if (other == mine)
                {
                    continue;
                }

                Dictionary<ThingDef, int> theirs = CountAggregate.Raw(() => other.resourceCounter.AllCountedAmounts);
                foreach (KeyValuePair<ThingDef, int> kv in theirs)
                {
                    merged.TryGetValue(kv.Key, out int have);
                    merged[kv.Key] = have + kv.Value;
                }
            }

            __result = merged;
        }
    }

    // 작업 지시서 '~개가 될 때까지': 작업대를 묶음의 다른 층에 잠깐 옮겨 둔 것처럼 해서(고스트) 바닐라 개수 세기를 그대로 돌려 더한다.
    // 특정 창고만 세도록 정한 지시서는 그 창고만 센다 (바닐라 그대로).
    [HarmonyPatch]
    internal static class RecipeWorkerCounter_Aggregate_Patch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (Type type in typeof(RecipeWorkerCounter).AllSubclasses().Concat(typeof(RecipeWorkerCounter)))
            {
                MethodInfo method = AccessTools.DeclaredMethod(type, nameof(RecipeWorkerCounter.CountProducts), new[] { typeof(Bill_Production) });
                if (method != null && !method.IsAbstract)
                {
                    yield return method;
                }
            }
        }

        private static void Postfix(RecipeWorkerCounter __instance, MethodBase __originalMethod, Bill_Production bill, ref int __result)
        {
            using YDMProfiler.Scope __perf = YDMProfiler.Measure(YDMProfiler.Cat.CountBills);
            if (CountAggregate.Computing || bill.GetIncludeSlotGroup() != null || !(bill.billStack?.billGiver is Thing giver) || !giver.Spawned)
            {
                return;
            }

            Map mine = giver.Map;
            List<Map> chain = CountAggregate.ChainOf(mine);
            if (chain == null)
            {
                return;
            }

            // 이 층 값도 합산 없이 다시 센다 (바닐라가 자원 수를 쓰면 이미 묶음 합산이 들어가 있으므로).
            object[] args = { bill };
            int total = CountAggregate.Raw(() => (int)__originalMethod.Invoke(__instance, args));
            foreach (Map other in chain)
            {
                if (other == mine)
                {
                    continue;
                }

                IntVec3 cell = LayerUtility.ArrivalCellOn(mine, other);
                total += CountAggregate.Raw(() => GhostUtility.RunThings(new[] { giver }, other, _ => cell, () => (int)__originalMethod.Invoke(__instance, args), 0));
            }

            __result = total;
        }
    }

    // 정착지 부(富): 이야기꾼 습격 규모 등이 쓰는 값. 지하 창고 물건도 정착지 부에 들어간다.
    [HarmonyPatch]
    internal static class WealthWatcher_Aggregate_Patch
    {
        private static readonly AccessTools.FieldRef<WealthWatcher, Map> MapRef = AccessTools.FieldRefAccess<WealthWatcher, Map>("map");

        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string name in new[] { "WealthTotal", "WealthItems", "WealthBuildings", "WealthFloorsOnly", "WealthPawns" })
            {
                MethodInfo getter = AccessTools.PropertyGetter(typeof(WealthWatcher), name);
                if (getter != null)
                {
                    yield return getter;
                }
            }
        }

        private static void Postfix(WealthWatcher __instance, MethodBase __originalMethod, ref float __result)
        {
            using YDMProfiler.Scope __perf = YDMProfiler.Measure(YDMProfiler.Cat.CountWealth);
            if (CountAggregate.Computing)
            {
                return;
            }

            Map mine = MapRef(__instance);
            List<Map> chain = CountAggregate.ChainOf(mine);
            if (chain == null)
            {
                return;
            }

            float total = __result;
            foreach (Map other in chain)
            {
                if (other != mine)
                {
                    total += CountAggregate.Raw(() => (float)__originalMethod.Invoke(other.wealthWatcher, null));
                }
            }

            __result = total;
        }
    }
}
