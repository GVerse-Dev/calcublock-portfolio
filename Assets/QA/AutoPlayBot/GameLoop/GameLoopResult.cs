#if IG_GAMELOOP_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using IGQA.AutoPlayBot.Metrics;
using UnityEngine;

namespace IGQA.AutoPlayBot
{
    /// <summary>
    /// Game Loop 세션 전체 결과. JsonUtility로 직렬화해 Firebase 결과 파일에 기록한다.
    /// </summary>
    [Serializable]
    internal sealed class GameLoopResult
    {
        // ── 누수 판정 임계값 ──────────────────────────────────────────────────
        private const int   WarmupGames              = 10;
        private const float PossibleLeakThresholdPct = 10f;
        private const float LeakThresholdPct         = 25f;

        // ── 세션 메타 ─────────────────────────────────────────────────────────
        public string timestamp;
        public int    scenario;
        public int    totalGames;
        public int    totalPlacements;

        // ── 점수/배치 통계 ─────────────────────────────────────────────────────
        public float avgScore;
        public float avgPlacementsPerGame;

        // ── 배치 시간 (ms) ─────────────────────────────────────────────────────
        public float p50Ms;
        public float p95Ms;
        public float p99Ms;
        public float maxMs;
        public float avgMs;

        // ── 메모리 추세 (MB) — GC 전 원시 스냅샷 ─────────────────────────────
        public float memStartMb;
        public float memEndMb;
        public float memGrowthPct;

        // ── post-GC 메모리 누수 판정 (scenario 2 전용) ────────────────────────
        public float  baselineMemMb;   // 게임 11~20 post-GC 평균
        public float  finalMemMb;      // 마지막 10게임 post-GC 평균
        public float  postGcGrowthPct; // (final - baseline) / baseline * 100
        public string leakVerdict;     // NO_LEAK / POSSIBLE_LEAK / LEAK

        // ── 예외 ──────────────────────────────────────────────────────────────
        public int      totalExceptions;
        public string[] exceptions;

        // ── 게임별 요약 ───────────────────────────────────────────────────────
        public GameRecord[] games;

        // ─────────────────────────────────────────────────────────────────────

        [Serializable]
        public sealed class GameRecord
        {
            public int   seed;
            public int   placements;
            public long  score;
            public bool  gameOver;
            public float avgMs;
            public long  maxMs;
            public int   exceptionCount;
            public float postGcMemoryMb; // scenario 2: GC 강제 후 측정값 (MB)
        }

        // ─────────────────────────────────────────────────────────────────────

        public static string Serialize(
            IReadOnlyList<BotSessionReport> reports,
            int scenario,
            IReadOnlyList<float> postGcMbPerGame = null)
        {
            var result = new GameLoopResult
            {
                timestamp   = DateTime.UtcNow.ToString("o"),
                scenario    = scenario,
                leakVerdict = "",
                exceptions  = Array.Empty<string>(),
                games       = Array.Empty<GameRecord>(),
            };

            if (reports == null || reports.Count == 0)
                return JsonUtility.ToJson(result, prettyPrint: true);

            // ── 게임별 요약 ───────────────────────────────────────────────────
            result.games = reports.Select((r, i) =>
            {
                long maxDur = r.PlacementDurations.Count > 0 ? r.PlacementDurations.Max() : 0L;
                double avg  = r.PlacementDurations.Count > 0 ? r.PlacementDurations.Average() : 0.0;
                return new GameRecord
                {
                    seed           = r.Seed,
                    placements     = r.PlacementCount,
                    score          = r.FinalScore,
                    gameOver       = r.ReachedGameOver,
                    avgMs          = (float)avg,
                    maxMs          = maxDur,
                    exceptionCount = r.Exceptions.Count,
                    postGcMemoryMb = postGcMbPerGame != null && i < postGcMbPerGame.Count
                                     ? postGcMbPerGame[i] : 0f,
                };
            }).ToArray();

            // ── 세션 집계 ─────────────────────────────────────────────────────
            result.totalGames           = reports.Count;
            result.totalPlacements      = reports.Sum(r => r.PlacementCount);
            result.avgScore             = (float)reports.Average(r => r.FinalScore);
            result.avgPlacementsPerGame = (float)reports.Average(r => r.PlacementCount);

            // ── 배치 시간 집계 ────────────────────────────────────────────────
            var sorted = reports
                .SelectMany(r => r.PlacementDurations)
                .OrderBy(d => d)
                .ToList();

            if (sorted.Count > 0)
            {
                result.p50Ms = Percentile(sorted, 0.50f);
                result.p95Ms = Percentile(sorted, 0.95f);
                result.p99Ms = Percentile(sorted, 0.99f);
                result.maxMs = sorted[sorted.Count - 1];
                result.avgMs = (float)sorted.Average();
            }

            // ── 원시 메모리 추세 (GC 전 스냅샷) ──────────────────────────────
            var firstSnaps = reports[0].MemorySnapshots;
            var lastSnaps  = reports[reports.Count - 1].MemorySnapshots;

            float startMb = firstSnaps.Count > 0
                ? firstSnaps[0].TotalMemoryBytes / 1048576f : 0f;
            float endMb = lastSnaps.Count > 0
                ? lastSnaps[lastSnaps.Count - 1].TotalMemoryBytes / 1048576f : 0f;

            result.memStartMb   = startMb;
            result.memEndMb     = endMb;
            result.memGrowthPct = startMb > 0f
                ? (endMb - startMb) / startMb * 100f : 0f;

            // ── post-GC 누수 판정 (scenario 2 전용) ───────────────────────────
            if (postGcMbPerGame != null && postGcMbPerGame.Count > WarmupGames + 1)
            {
                int n = postGcMbPerGame.Count;

                // baseline: warmup 이후 10게임 평균
                int baselineStart = WarmupGames;
                int baselineEnd   = Mathf.Min(baselineStart + 10, n);
                float baseline    = 0f;
                for (int i = baselineStart; i < baselineEnd; i++) baseline += postGcMbPerGame[i];
                baseline /= (baselineEnd - baselineStart);

                // final: 마지막 10게임 평균
                int finalStart = Mathf.Max(n - 10, baselineEnd);
                float final_   = 0f;
                for (int i = finalStart; i < n; i++) final_ += postGcMbPerGame[i];
                final_ /= (n - finalStart);

                float growth = baseline > 0f ? (final_ - baseline) / baseline * 100f : 0f;

                result.baselineMemMb    = baseline;
                result.finalMemMb       = final_;
                result.postGcGrowthPct  = growth;
                result.leakVerdict      = growth < PossibleLeakThresholdPct ? "NO_LEAK"
                                        : growth < LeakThresholdPct         ? "POSSIBLE_LEAK"
                                        :                                     "LEAK";
            }

            // ── 예외 집계 ─────────────────────────────────────────────────────
            result.exceptions = reports
                .SelectMany(r => r.Exceptions)
                .Select(e => e.ToString())
                .ToArray();
            result.totalExceptions = result.exceptions.Length;

            return JsonUtility.ToJson(result, prettyPrint: true);
        }

        private static float Percentile(List<long> sorted, float p)
        {
            int idx = Mathf.Clamp(
                (int)Math.Ceiling(p * sorted.Count) - 1,
                0, sorted.Count - 1);
            return sorted[idx];
        }
    }
}
#endif
