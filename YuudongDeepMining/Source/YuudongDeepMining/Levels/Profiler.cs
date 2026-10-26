using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace YuudongDeepMining
{
    // 성능 측정: 이 모드 코드가 범주별로 몇 번 불리고 얼마나 걸리는지 잰다 (설정에서 켬).
    // 겹친 측정은 안쪽 시간을 바깥에서 빼서 '자기 시간'만 센다 (예: 일 판단 안의 운반 판단은 운반 판단으로만 센다).
    // 메인 스레드에서만 잰다. 꺼져 있으면 범주 확인 한 번만 하고 끝난다.
    public static class YDMProfiler
    {
        public enum Cat
        {
            WorkProbe,
            NeedProbe,
            HaulScan,
            BillSupply,
            RescueScan,
            AnimalFollow,
            Travel,
            CountResources,
            CountBills,
            CountWealth,
            AlertPawns,
            Raids,
            Infestation,
            LevelPanel,
            Chain,
        }

        private static readonly int Count = Enum.GetValues(typeof(Cat)).Length;

        private static readonly long[] ticks = new long[Count];
        private static readonly int[] calls = new int[Count];
        private static readonly long[] logTicks = new long[Count];
        private static readonly int[] logCalls = new int[Count];

        // 최근 1초 결과 (화면 표시용)
        public static readonly double[] LastMs = new double[Count];
        public static readonly int[] LastCalls = new int[Count];

        private static long windowStart = Stopwatch.GetTimestamp();
        private static long logStart = Stopwatch.GetTimestamp();

        private struct Frame
        {
            public int cat;
            public long start;
            public long child;
        }

        private static readonly List<Frame> stack = new List<Frame>();

        public static bool On => DeepMiningMod.Settings.profiler;

        public static Scope Measure(Cat cat)
        {
            if (!On || !UnityData.IsInMainThread)
            {
                return default;
            }

            stack.Add(new Frame { cat = (int)cat, start = Stopwatch.GetTimestamp() });
            return new Scope(stack.Count);
        }

        public readonly struct Scope : IDisposable
        {
            private readonly int depth;

            public Scope(int depth)
            {
                this.depth = depth;
            }

            public void Dispose()
            {
                if (depth == 0 || stack.Count != depth)
                {
                    return;
                }

                Frame f = stack[depth - 1];
                stack.RemoveAt(depth - 1);
                long elapsed = Stopwatch.GetTimestamp() - f.start;
                ticks[f.cat] += Math.Max(0, elapsed - f.child);
                calls[f.cat]++;
                if (stack.Count > 0)
                {
                    Frame parent = stack[stack.Count - 1];
                    parent.child += elapsed;
                    stack[stack.Count - 1] = parent;
                }
            }
        }

        // 1초마다 화면용 값을 갱신하고, 10초마다 로그를 남긴다.
        public static void Update()
        {
            if (!On)
            {
                return;
            }

            long now = Stopwatch.GetTimestamp();
            double seconds = (now - windowStart) / (double)Stopwatch.Frequency;
            if (seconds >= 1.0)
            {
                for (int i = 0; i < Count; i++)
                {
                    LastMs[i] = ticks[i] * 1000.0 / Stopwatch.Frequency / seconds;
                    LastCalls[i] = (int)Math.Round(calls[i] / seconds);
                    logTicks[i] += ticks[i];
                    logCalls[i] += calls[i];
                    ticks[i] = 0;
                    calls[i] = 0;
                }

                windowStart = now;
            }

            double logSeconds = (now - logStart) / (double)Stopwatch.Frequency;
            if (logSeconds >= 10.0)
            {
                StringBuilder sb = new StringBuilder("[YDM] perf (" + logSeconds.ToString("0") + "s, ms per second / calls per second):");
                foreach (int i in Enumerable.Range(0, Count).OrderByDescending(i => logTicks[i]))
                {
                    if (logCalls[i] == 0)
                    {
                        continue;
                    }

                    sb.Append("  " + (Cat)i + " " + (logTicks[i] * 1000.0 / Stopwatch.Frequency / logSeconds).ToString("0.00") + "ms/" + (logCalls[i] / logSeconds).ToString("0"));
                }

                Log.Message(sb.ToString());
                Array.Clear(logTicks, 0, Count);
                Array.Clear(logCalls, 0, Count);
                logStart = now;
            }
        }

        public static double TotalMs => LastMs.Sum();

        // 층 패널 위에 그리는 측정 결과 (최근 1초, 자기 시간 큰 순).
        public static void Draw(Rect panel)
        {
            if (!On)
            {
                return;
            }

            List<int> rows = Enumerable.Range(0, Count).Where(i => LastCalls[i] > 0 || LastMs[i] > 0.0005).OrderByDescending(i => LastMs[i]).Take(10).ToList();
            float lineH = 18f;
            float height = 26f + Mathf.Max(1, rows.Count) * lineH;
            float width = 230f;
            Rect outer = new Rect(panel.xMax - width, panel.y - height - 6f, width, height);
            Widgets.DrawWindowBackground(outer);
            Rect inner = outer.ContractedBy(5f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, lineH), "YDM.Perf.Title".Translate(TotalMs.ToString("0.00")));
            float y = inner.y + lineH + 2f;
            if (rows.Count == 0)
            {
                Widgets.Label(new Rect(inner.x, y, inner.width, lineH), "-");
            }

            foreach (int i in rows)
            {
                Rect row = new Rect(inner.x, y, inner.width, lineH);
                Widgets.Label(row, ("YDM.Perf.Cat." + (Cat)i).Translate());
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(row, LastMs[i].ToString("0.00") + "ms  " + LastCalls[i] + "/s");
                Text.Anchor = TextAnchor.MiddleLeft;
                y += lineH;
            }

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }
    }
}
