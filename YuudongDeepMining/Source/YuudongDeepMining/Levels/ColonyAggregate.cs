using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace YuudongDeepMining
{
    // 층 묶음을 한 정착지로 (지도 단위 집계): 알림은 지도마다 '그 지도의 정착민·죄수 목록'을 세서 판단한다.
    // 알림을 계산하는 동안에만, 갱도 줄의 맨 위 지도가 줄 전체의 목록을 대표하고 아래층들은 빈 목록을 낸다.
    // 그래서 개별 알림을 고치지 않아도 '간수 필요'처럼 층마다 따로 세던 알림이 층 묶음 전체를 기준으로 판단한다.
    public static class ColonyAggregate
    {
        private static int alertDepth;

        private static bool computing;

        public static readonly AccessTools.FieldRef<MapPawns, Map> MapRef = AccessTools.FieldRefAccess<MapPawns, Map>("map");

        public static bool Active => alertDepth > 0 && !computing && !GhostUtility.Active;

        public static void Enter()
        {
            alertDepth++;
        }

        public static void Exit()
        {
            alertDepth--;
        }

        // 묶음에 속한 지도면 묶음(맨 위가 첫째)을, 아니면 null.
        public static List<Map> ChainOf(MapPawns mapPawns)
        {
            Map map = MapRef(mapPawns);
            if (map == null)
            {
                return null;
            }

            List<Map> chain = LayerUtility.Chain(map);
            return chain.Count < 2 ? null : chain;
        }

        public static T Compute<T>(System.Func<T> func)
        {
            computing = true;
            try
            {
                return func();
            }
            finally
            {
                computing = false;
            }
        }
    }

    [HarmonyPatch(typeof(Alert), nameof(Alert.Recalculate))]
    internal static class Alert_Recalculate_Aggregate_Patch
    {
        private static void Prefix()
        {
            ColonyAggregate.Enter();
        }

        private static void Finalizer()
        {
            ColonyAggregate.Exit();
        }
    }

    // 정착민·죄수·노예·가축 목록: 맨 위 지도는 묶음 전체를 합친 목록, 아래층은 빈 목록.
    [HarmonyPatch]
    internal static class MapPawns_List_Aggregate_Patch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string name in new[] { "FreeColonists", "PrisonersOfColony", "FreeColonistsSpawned", "PrisonersOfColonySpawned", "SlavesOfColonySpawned", "FreeColonistsAndPrisonersSpawned", "SpawnedColonyAnimals" })
            {
                MethodInfo getter = AccessTools.PropertyGetter(typeof(MapPawns), name);
                if (getter != null)
                {
                    yield return getter;
                }
            }
        }

        private static void Postfix(MapPawns __instance, MethodBase __originalMethod, ref List<Pawn> __result)
        {
            if (!ColonyAggregate.Active)
            {
                return;
            }

            List<Map> chain = ColonyAggregate.ChainOf(__instance);
            if (chain == null)
            {
                return;
            }

            if (ColonyAggregate.MapRef(__instance) != chain[0])
            {
                __result = new List<Pawn>();
                return;
            }

            List<Pawn> mine = __result;
            __result = ColonyAggregate.Compute(delegate
            {
                List<Pawn> all = new List<Pawn>(mine);
                foreach (Map other in chain.Skip(1))
                {
                    if (__originalMethod.Invoke(other.mapPawns, null) is List<Pawn> list)
                    {
                        all.AddRange(list);
                    }
                }

                return all;
            });
        }
    }

    // 수 세기: 맨 위 지도는 묶음 전체의 합, 아래층은 0.
    [HarmonyPatch]
    internal static class MapPawns_Count_Aggregate_Patch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string name in new[] { "FreeColonistsCount", "FreeColonistsSpawnedCount", "PrisonersOfColonySpawnedCount", "ColonistsSpawnedCount" })
            {
                MethodInfo getter = AccessTools.PropertyGetter(typeof(MapPawns), name);
                if (getter != null)
                {
                    yield return getter;
                }
            }
        }

        private static void Postfix(MapPawns __instance, MethodBase __originalMethod, ref int __result)
        {
            if (!ColonyAggregate.Active)
            {
                return;
            }

            List<Map> chain = ColonyAggregate.ChainOf(__instance);
            if (chain == null)
            {
                return;
            }

            if (ColonyAggregate.MapRef(__instance) != chain[0])
            {
                __result = 0;
                return;
            }

            int mine = __result;
            __result = ColonyAggregate.Compute(() => mine + chain.Skip(1).Sum(m => (int)__originalMethod.Invoke(m.mapPawns, null)));
        }
    }

    // 있는지 여부: 맨 위 지도는 묶음 어디에든 있으면 참, 아래층은 거짓.
    [HarmonyPatch]
    internal static class MapPawns_Any_Aggregate_Patch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string name in new[] { "AnyColonistSpawned", "AnyFreeColonistSpawned" })
            {
                MethodInfo getter = AccessTools.PropertyGetter(typeof(MapPawns), name);
                if (getter != null)
                {
                    yield return getter;
                }
            }
        }

        private static void Postfix(MapPawns __instance, MethodBase __originalMethod, ref bool __result)
        {
            if (!ColonyAggregate.Active)
            {
                return;
            }

            List<Map> chain = ColonyAggregate.ChainOf(__instance);
            if (chain == null)
            {
                return;
            }

            if (ColonyAggregate.MapRef(__instance) != chain[0])
            {
                __result = false;
                return;
            }

            bool mine = __result;
            __result = mine || ColonyAggregate.Compute(() => chain.Skip(1).Any(m => (bool)__originalMethod.Invoke(m.mapPawns, null)));
        }
    }

    // 층 판단 디버그 로그 (설정에서 켬). 같은 림·같은 내용은 잠시 한 번만 남긴다.
    public static class LayerDebug
    {
        // 화면 조작(우클릭 등) 디버그: 같은 내용을 반복해도 매번 남긴다.
        public static void LogUI(string message)
        {
            if (DeepMiningMod.Settings.debugLog)
            {
                Verse.Log.Message("[YDM] UI: " + message);
            }
        }

        private static readonly Dictionary<string, int> last = new Dictionary<string, int>();

        public static void Log(Pawn pawn, string message)
        {
            if (!DeepMiningMod.Settings.debugLog)
            {
                return;
            }

            string key = pawn.ThingID + message;
            int now = Find.TickManager.TicksGame;
            if (last.TryGetValue(key, out int tick) && now - tick < 2500)
            {
                return;
            }

            if (last.Count > 2048)
            {
                last.Clear();
            }

            last[key] = now;
            Verse.Log.Message("[YDM] " + pawn.LabelShort + " (" + LayerUtility.LayerLabel(LayerUtility.DepthOf(pawn.Map)) + "): " + message);
        }
    }
}
