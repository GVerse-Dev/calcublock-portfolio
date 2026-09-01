using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using IGMain;
using IGQA.AutoPlayBot.Metrics;
using IGQA.AutoPlayBot.Strategies;
using NUnit.Framework;
using Unity.Profiling.Memory;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace IGQA.AutoPlayBot.Tests
{
    /// <summary>
    /// 누수 원인 특정용 Memory Profiler 스냅샷 자동 캡처.
    ///
    /// 실행: Window > General > Test Runner > PlayMode 탭
    ///       > LeakSnapshotCapture > CaptureLeakSnapshots > Run
    ///
    /// 완료 후 프로젝트 루트 MemoryCaptures/ 폴더에 .snap 2장 생성.
    /// Memory Profiler 창(Window > Analysis > Memory Profiler)에서 두 파일을 열어
    /// Diff 기능으로 game 5 → game 55 증가 객체를 확인한다.
    /// </summary>
    public class LeakSnapshotCapture
    {
        private const string TitleSceneName  = "TitleScene";
        private const string IGSceneName     = "IGScene";
        private const int    BotSeed         = 42000;
        private const int    SnapshotAtGame1 = 5;
        private const int    SnapshotAtGame2 = 55;
        private const int    TotalGames      = SnapshotAtGame2; // 55판 완료 후 종료

        private static readonly CaptureFlags SnapFlags =
            CaptureFlags.ManagedObjects | CaptureFlags.NativeObjects;

        // ── Test ─────────────────────────────────────────────────────────────

        [UnityTest]
        [Timeout(600_000)] // 10분 — 55게임 실시간 + 스냅샷 2회 여유
        public IEnumerator CaptureLeakSnapshots()
        {
            // ── 씬 로드 ───────────────────────────────────────────────────────
            yield return SceneManager.LoadSceneAsync(TitleSceneName);
            for (int i = 0; i < 5; i++) yield return null;

            yield return SceneManager.LoadSceneAsync(IGSceneName);
            for (int i = 0; i < 5; i++) yield return null;

            var gameController = UnityEngine.Object.FindAnyObjectByType<IGGameController>(
                FindObjectsInactive.Include);
            Assert.IsNotNull(gameController,
                "IGGameController을 씬에서 찾지 못했습니다. TitleScene→IGScene 로드 순서를 확인하세요.");

            // ── MemoryCaptures 폴더 준비 ──────────────────────────────────────
            string captureDir = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "MemoryCaptures"));
            Directory.CreateDirectory(captureDir);

            // ── 봇 실행 + 스냅샷 ─────────────────────────────────────────────
            var strategy = new RandomPlacementStrategy();
            var bot      = new AutoPlayBot(gameController, strategy, seed: BotSeed);
            var reports  = new List<BotSessionReport>();

            int    gamesDone = 0;
            string snap1Path = null;
            string snap2Path = null;

            yield return bot.PlayRealtime(
                maxGames:           TotalGames,
                maxDurationSeconds: 3600f,
                onGameComplete:     r => reports.Add(r),
                afterEachGame: () =>
                {
                    gamesDone++;
                    if (gamesDone == SnapshotAtGame1)
                        return TakeSnapshotCoroutine(captureDir, gamesDone,
                            path => snap1Path = path);
                    if (gamesDone == SnapshotAtGame2)
                        return TakeSnapshotCoroutine(captureDir, gamesDone,
                            path => snap2Path = path);
                    return null;
                });

            // ── 검증 ─────────────────────────────────────────────────────────
            Assert.IsNotNull(snap1Path,
                $"스냅샷 A (game {SnapshotAtGame1}) 캡처 실패 — logcat에서 [LeakSnap] 오류 확인");
            Assert.IsTrue(File.Exists(snap1Path),  $"파일 없음: {snap1Path}");
            Assert.Greater(new FileInfo(snap1Path).Length, 0L, "스냅샷 A가 빈 파일");

            Assert.IsNotNull(snap2Path,
                $"스냅샷 B (game {SnapshotAtGame2}) 캡처 실패 — logcat에서 [LeakSnap] 오류 확인");
            Assert.IsTrue(File.Exists(snap2Path),  $"파일 없음: {snap2Path}");
            Assert.Greater(new FileInfo(snap2Path).Length, 0L, "스냅샷 B가 빈 파일");

            Debug.Log($"[LeakSnap] 완료 ({reports.Count}판)\n" +
                      $"  A: {snap1Path}\n" +
                      $"  B: {snap2Path}");
        }

        // ── 내부 헬퍼 ────────────────────────────────────────────────────────

        private IEnumerator TakeSnapshotCoroutine(
            string dir, int gameCount, Action<string> onSaved)
        {
            // 수거 가능한 가비지를 먼저 제거해 스냅샷에 진짜 누수만 남긴다
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            yield return null;

            float memMb = GC.GetTotalMemory(false) / 1048576f;
            Debug.Log($"[LeakSnap] Game {gameCount} post-GC: {memMb:F2}MB — taking snapshot...");

            string snapPath = Path.Combine(dir, $"snapshot_after_{gameCount:D3}games.snap");
            bool   done    = false;
            bool   success = false;

            MemoryProfiler.TakeSnapshot(
                snapPath,
                (path, ok) =>
                {
                    success = ok;
                    if (ok) onSaved?.Invoke(path);
                    done = true;
                },
                SnapFlags);

            yield return new WaitUntil(() => done);

            if (success)
                Debug.Log($"[LeakSnap] Snapshot saved → {snapPath}");
            else
                Debug.LogError($"[LeakSnap] Snapshot FAILED at game {gameCount}");
        }
    }
}
