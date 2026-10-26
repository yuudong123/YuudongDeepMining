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

        // 합산 결과를 잠깐 기억할지: 게임 시간 30틱 또는 실제 0.25초가 지나면 다시 센다 (일시정지 중에도 갱신되게).
        public static bool Stale(ref int stampTicks, ref long stampMs)
        {
            int ticks = Find.TickManager.TicksGame;
            long ms = Environment.TickCount64;
            if (stampMs != 0 && ticks >= stampTicks && ticks - stampTicks < 30 && ms - stampMs < 250)
            {
                return false;
            }

            stampTicks = ticks;
            stampMs = ms;
            return true;
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

    // 자원 수: 갱도 줄마다 각 층의 원래 자원 사전을 한 번 합쳐 기억해 두고(잠깐), 개별 질문은 거기서 바로 꺼낸다.
    // (GetCountIn(카테고리)는 GetCount를 다시 부르므로 자동으로 따라온다.)
    public static class ResourceAggregate
    {
        private class Entry
        {
            public int stampTicks;
            public long stampMs;
            public readonly Dictionary<ThingDef, int> merged = new Dictionary<ThingDef, int>();
            public float humanNutrition;
            public float babyNutrition;
        }

        private static readonly Dictionary<Map, Entry> entries = new Dictionary<Map, Entry>();

        public static readonly AccessTools.FieldRef<ResourceCounter, Map> MapRef = AccessTools.FieldRefAccess<ResourceCounter, Map>("map");

        // 묶음에 속한 지도면 합친 자원 사전을, 아니면 null.
        public static Dictionary<ThingDef, int> Merged(ResourceCounter counter, out float human, out float baby)
        {
            human = 0f;
            baby = 0f;
            if (CountAggregate.Computing)
            {
                return null;
            }

            List<Map> chain = CountAggregate.ChainOf(MapRef(counter));
            if (chain == null)
            {
                return null;
            }

            Map root = chain[0];
            if (!entries.TryGetValue(root, out Entry e))
            {
                if (entries.Count > 16)
                {
                    entries.Clear();
                }

                entries[root] = e = new Entry();
            }

            if (CountAggregate.Stale(ref e.stampTicks, ref e.stampMs))
            {
                e.merged.Clear();
                foreach (Map m in chain)
                {
                    Dictionary<ThingDef, int> theirs = CountAggregate.Raw(() => m.resourceCounter.AllCountedAmounts);
                    foreach (KeyValuePair<ThingDef, int> kv in theirs)
                    {
                        e.merged.TryGetValue(kv.Key, out int have);
                        e.merged[kv.Key] = have + kv.Value;
                    }
                }

                // 바닐라 영양 합계와 같은 식.
                e.humanNutrition = 0f;
                e.babyNutrition = 0f;
                foreach (KeyValuePair<ThingDef, int> kv in e.merged)
                {
                    if (kv.Key.IsNutritionGivingIngestible && kv.Key.ingestible.HumanEdible)
                    {
                        e.humanNutrition += kv.Key.GetStatValueAbstract(StatDefOf.Nutrition) * kv.Value;
                    }

                    if (kv.Key.IsNutritionGivingIngestibleForHumanlikeBabies)
                    {
                        e.babyNutrition += kv.Key.GetStatValueAbstract(StatDefOf.Nutrition) * kv.Value;
                    }
                }
            }

            human = e.humanNutrition;
            baby = e.babyNutrition;
            return e.merged;
        }
    }

    [HarmonyPatch(typeof(ResourceCounter), nameof(ResourceCounter.GetCount), new[] { typeof(ThingDef) })]
    internal static class ResourceCounter_GetCount_Aggregate_Patch
    {
        private static void Postfix(ResourceCounter __instance, ThingDef rDef, ref int __result)
        {
            using YDMProfiler.Scope __perf = YDMProfiler.Measure(YDMProfiler.Cat.CountResources);
            if (rDef == null || rDef.resourceReadoutPriority == ResourceCountPriority.Uncounted)
            {
                return;
            }

            Dictionary<ThingDef, int> merged = ResourceAggregate.Merged(__instance, out float _, out float _);
            if (merged != null)
            {
                __result = merged.TryGetValue(rDef, out int n) ? n : 0;
            }
        }
    }

    [HarmonyPatch(typeof(ResourceCounter), nameof(ResourceCounter.GetCountIn), new[] { typeof(ThingRequestGroup) })]
    internal static class ResourceCounter_GetCountIn_Aggregate_Patch
    {
        private static void Postfix(ResourceCounter __instance, ThingRequestGroup group, ref int __result)
        {
            using YDMProfiler.Scope __perf = YDMProfiler.Measure(YDMProfiler.Cat.CountResources);
            Dictionary<ThingDef, int> merged = ResourceAggregate.Merged(__instance, out float _, out float _);
            if (merged == null)
            {
                return;
            }

            int n = 0;
            foreach (KeyValuePair<ThingDef, int> kv in merged)
            {
                if (group.Includes(kv.Key))
                {
                    n += kv.Value;
                }
            }

            __result = n;
        }
    }

    [HarmonyPatch(typeof(ResourceCounter), nameof(ResourceCounter.TotalHumanEdibleNutrition), MethodType.Getter)]
    internal static class ResourceCounter_HumanNutrition_Aggregate_Patch
    {
        private static void Postfix(ResourceCounter __instance, ref float __result)
        {
            using YDMProfiler.Scope __perf = YDMProfiler.Measure(YDMProfiler.Cat.CountResources);
            if (ResourceAggregate.Merged(__instance, out float human, out float _) != null)
            {
                __result = human;
            }
        }
    }

    [HarmonyPatch(typeof(ResourceCounter), nameof(ResourceCounter.TotalHumanBabyEdibleNutrition), MethodType.Getter)]
    internal static class ResourceCounter_BabyNutrition_Aggregate_Patch
    {
        private static void Postfix(ResourceCounter __instance, ref float __result)
        {
            using YDMProfiler.Scope __perf = YDMProfiler.Measure(YDMProfiler.Cat.CountResources);
            if (ResourceAggregate.Merged(__instance, out float _, out float baby) != null)
            {
                __result = baby;
            }
        }
    }

    // 왼쪽 위 자원 표시 등이 쓰는 전체 목록: 묶음 전체를 합친 사전 (읽기 전용으로 쓴다).
    [HarmonyPatch(typeof(ResourceCounter), nameof(ResourceCounter.AllCountedAmounts), MethodType.Getter)]
    internal static class ResourceCounter_AllCounted_Aggregate_Patch
    {
        private static void Postfix(ResourceCounter __instance, ref Dictionary<ThingDef, int> __result)
        {
            using YDMProfiler.Scope __perf = YDMProfiler.Measure(YDMProfiler.Cat.CountResources);
            Dictionary<ThingDef, int> merged = ResourceAggregate.Merged(__instance, out float _, out float _);
            if (merged != null)
            {
                __result = merged;
            }
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

        private class Entry
        {
            public int stampTicks;
            public long stampMs;
            public readonly float[] sums = new float[5];
        }

        private static readonly Dictionary<Map, Entry> entries = new Dictionary<Map, Entry>();

        private static int IndexOf(MethodBase method)
        {
            switch (method.Name)
            {
                case "get_WealthTotal": return 0;
                case "get_WealthItems": return 1;
                case "get_WealthBuildings": return 2;
                case "get_WealthFloorsOnly": return 3;
                default: return 4;
            }
        }

        // 갱도 줄마다 각 층의 원래 부(富) 값을 잠깐 기억해 두고 더해서 돌려준다.
        private static void Postfix(WealthWatcher __instance, MethodBase __originalMethod, ref float __result)
        {
            using YDMProfiler.Scope __perf = YDMProfiler.Measure(YDMProfiler.Cat.CountWealth);
            if (CountAggregate.Computing)
            {
                return;
            }

            List<Map> chain = CountAggregate.ChainOf(MapRef(__instance));
            if (chain == null)
            {
                return;
            }

            Map root = chain[0];
            if (!entries.TryGetValue(root, out Entry e))
            {
                if (entries.Count > 16)
                {
                    entries.Clear();
                }

                entries[root] = e = new Entry();
            }

            if (CountAggregate.Stale(ref e.stampTicks, ref e.stampMs))
            {
                Array.Clear(e.sums, 0, e.sums.Length);
                foreach (Map m in chain)
                {
                    WealthWatcher w = m.wealthWatcher;
                    CountAggregate.Raw(delegate
                    {
                        e.sums[0] += w.WealthTotal;
                        e.sums[1] += w.WealthItems;
                        e.sums[2] += w.WealthBuildings;
                        e.sums[3] += w.WealthFloorsOnly;
                        e.sums[4] += w.WealthPawns;
                        return 0;
                    });
                }
            }

            __result = e.sums[IndexOf(__originalMethod)];
        }
    }
}
