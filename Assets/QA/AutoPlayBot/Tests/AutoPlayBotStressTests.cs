using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using IGMain;
using IGQA.AutoPlayBot;
using IGQA.AutoPlayBot.Metrics;
using IGQA.AutoPlayBot.Strategies;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace IGQA.AutoPlayBot.Tests
{
    /// <summary>
    /// 자동 플레이 봇으로 N게임을 돌리며 예외·메모리·배치 시간을 검증하는 스트레스 테스트.
    /// Editor-only 어셈블리이므로 빌드에 포함되지 않는다.
    /// </summary>
    public class AutoPlayBotStressTests
    {
        // ── 씬 이름 ──────────────────────────────────────────────────────────
        private const string TitleSceneName = "TitleScene";
        private const string IGSceneName    = "IGScene";

        // ── 임계값 상수 ───────────────────────────────────────────────────────
        private const int   GameCount            = 10;
        private const int   MaxPlacementsPerGame = 2000;

        // 메모리 누수 판정: 첫 게임 종료 메모리 대비 마지막 게임 종료 메모리 증가율
        private const float MemoryGrowthThreshold = 0.50f; // 50%

        // 프레임 예산(60fps 기준)
        private const double FrameBudgetMs = 1000.0 / 60.0; // ≈16.67ms
        private const int    WarmupPlacements = 10; // JIT 워밍업 제외

        // ── 공유 픽스처 ───────────────────────────────────────────────────────
        private IGGameController        _gameController;
        private RandomPlacementStrategy _strategy;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync(TitleSceneName);
            for (int i = 0; i < 5; i++) yield return null;

            yield return SceneManager.LoadSceneAsync(IGSceneName);
            for (int i = 0; i < 5; i++) yield return null;

            _gameController = UnityEngine.Object.FindAnyObjectByType<IGGameController>(
                FindObjectsInactive.Include);

            Assert.IsNotNull(_gameController,
                "IGGameController을 씬에서 찾지 못했습니다. TitleScene→IGScene 로드 순서를 확인하세요.");

            _strategy = new RandomPlacementStrategy();
        }

        // ── Test 1: 10게임 예외 없음 ──────────────────────────────────────────

        [UnityTest]
        public IEnumerator AutoPlayBot_TenGames_NoUnhandledExceptions()
        {
            var failMessages = new List<string>();

            for (int game = 0; game < GameCount; game++)
            {
                _gameController.RestartGame();
                yield return null;

                int seed   = 1000 + game * 7919;
                var bot    = new AutoPlayBot(_gameController, _strategy, seed);
                var report = bot.Play(MaxPlacementsPerGame);

                Debug.Log(report.ToSummary());

                foreach (var ex in report.Exceptions)
                    failMessages.Add($"[Game {game}] seed={seed}\n{ex}");
            }

            if (failMessages.Count > 0)
                Assert.Fail("예외 발생 — 아래 seed로 재현 가능합니다:\n" +
                            string.Join("\n---\n", failMessages));
        }

        // ── Test 2: 다수 게임 메모리 누수 없음 ───────────────────────────────
        // 각 게임 종료 직전 GC.Collect() 후 메모리를 측정해 선두/말미를 비교한다.
        // 단일 긴 세션 대신 게임 반복으로 테스트하므로 짧게 끝나는 random 전략과도 호환된다.

        [UnityTest]
        public IEnumerator AutoPlayBot_MultipleGames_NoMemoryLeak()
        {
            const int memGameCount = 20;
            var endMemories = new List<long>(memGameCount);

            for (int game = 0; game < memGameCount; game++)
            {
                _gameController.RestartGame();
                yield return null;

                int seed   = 2000 + game * 6271;
                var bot    = new AutoPlayBot(_gameController, _strategy, seed);
                bot.Play(MaxPlacementsPerGame);

                // 게임 종료 후 GC 강제 수행, 살아있는 메모리만 남긴다
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                endMemories.Add(GC.GetTotalMemory(false));

                yield return null;
            }

            long firstMem = endMemories[0];
            long lastMem  = endMemories[endMemories.Count - 1];
            double growth = firstMem > 0 ? (double)(lastMem - firstMem) / firstMem : 0;

            Debug.Log($"[MemLeakTest] games={memGameCount}  " +
                      $"first={firstMem / 1048576.0:F2}MB  " +
                      $"last={lastMem / 1048576.0:F2}MB  " +
                      $"growth={growth * 100:F1}%");

            Assert.Less(growth, (double)MemoryGrowthThreshold,
                $"메모리 증가율 {growth * 100:F1}% ≥ 임계값 {MemoryGrowthThreshold * 100}%. " +
                $"first={firstMem / 1048576.0:F2}MB  last={lastMem / 1048576.0:F2}MB");
        }

        // ── Test 3: 배치 시간 프레임 예산 이내 ───────────────────────────────
        // JIT 워밍업 후 10게임 누적 데이터로 판정.
        // p50 < 1프레임(≈16.67ms), p95 < 2프레임(≈33.34ms), p99 < 3프레임(≈50ms).

        [UnityTest]
        public IEnumerator AutoPlayBot_PlacementDuration_StaysWithinFrameBudget()
        {
            var allDurations = new List<long>();      // (durationMs, placementGlobalIndex)
            var allGcAllocs  = new List<(int globalIdx, long durationMs, long gcAlloc)>();
            int globalPlacementIndex = 0;

            for (int game = 0; game < GameCount; game++)
            {
                _gameController.RestartGame();
                yield return null;

                int seed   = 777 + game * 1009;
                var bot    = new AutoPlayBot(_gameController, _strategy, seed);
                var report = bot.Play(MaxPlacementsPerGame);

                Debug.Log(report.ToSummary());

                for (int i = 0; i < report.PlacementDurations.Count; i++)
                {
                    globalPlacementIndex++;
                    if (globalPlacementIndex <= WarmupPlacements) continue; // JIT 워밍업 제외

                    long dur = report.PlacementDurations[i];
                    long gc  = i < report.PlacementGcAllocBytes.Count
                        ? report.PlacementGcAllocBytes[i] : 0L;

                    allDurations.Add(dur);
                    allGcAllocs.Add((globalPlacementIndex, dur, gc));
                }
            }

            if (allDurations.Count == 0)
            {
                Assert.Inconclusive(
                    $"워밍업 제외 후 배치 기록이 없습니다. " +
                    $"WarmupPlacements({WarmupPlacements})을 줄이거나 게임 수를 늘리세요.");
                yield break;
            }

            var sorted = allDurations.OrderBy(d => d).ToList();

            long p50 = Percentile(sorted, 0.50);
            long p95 = Percentile(sorted, 0.95);
            long p99 = Percentile(sorted, 0.99);
            double avg = allDurations.Average();
            long   max = sorted[sorted.Count - 1];

            // 가장 느린 상위 5개 (디버깅용)
            var top5 = allGcAllocs.OrderByDescending(t => t.durationMs).Take(5).ToList();
            var top5Str = string.Join(", ",
                top5.Select(t => $"idx={t.globalIdx} dur={t.durationMs}ms gc={t.gcAlloc / 1024}KB"));

            Debug.Log($"[PerfTest] n={allDurations.Count}  " +
                      $"p50={p50}ms  p95={p95}ms  p99={p99}ms  " +
                      $"avg={avg:F2}ms  max={max}ms");
            Debug.Log($"[PerfTest] top5_slowest: {top5Str}");

            double budget1 = FrameBudgetMs;
            double budget2 = FrameBudgetMs * 2;
            double budget3 = FrameBudgetMs * 3;

            var failures = new List<string>();
            if (p50 >= (long)Math.Ceiling(budget1))
                failures.Add($"p50={p50}ms ≥ 1프레임({budget1:F2}ms) — 중앙값이 느림");
            if (p95 >= (long)Math.Ceiling(budget2))
                failures.Add($"p95={p95}ms ≥ 2프레임({budget2:F2}ms)");
            if (p99 >= (long)Math.Ceiling(budget3))
                failures.Add($"p99={p99}ms ≥ 3프레임({budget3:F2}ms)");

            if (failures.Count > 0)
                Assert.Fail(
                    string.Join("\n", failures) +
                    $"\navg={avg:F2}ms max={max}ms n={allDurations.Count}" +
                    $"\ntop5_slowest: {top5Str}");
        }

        // ── 유틸 ──────────────────────────────────────────────────────────────

        private static long Percentile(List<long> sorted, double p)
        {
            int idx = Mathf.Clamp(
                (int)Math.Ceiling(p * sorted.Count) - 1,
                0, sorted.Count - 1);
            return sorted[idx];
        }
    }
}
