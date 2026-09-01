using System;
using System.Collections;
using System.Diagnostics;
using IGMain;
using IGQA.AutoPlayBot.Metrics;
using IGQA.AutoPlayBot.Strategies;
using UnityEngine;

namespace IGQA.AutoPlayBot
{
    /// <summary>
    /// CalculationTetris 자동 플레이 봇.
    /// IGGameController의 public API만 사용해 게임오버까지 블록을 배치하고
    /// 세션 리포트를 반환한다.
    ///
    /// 동일한 seed → 동일한 결과를 보장한다 (System.Random 시드 고정).
    /// </summary>
    public sealed class AutoPlayBot : IAutoPlayBot
    {
        private readonly IGGameController   _game;
        private readonly IPlacementStrategy _strategy;
        private readonly int                _seed;

        public AutoPlayBot(IGGameController game, IPlacementStrategy strategy, int seed)
        {
            _game     = game     ?? throw new ArgumentNullException(nameof(game));
            _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
            _seed     = seed;
        }

        // ── 동기 플레이 (EditMode/PlayMode 테스트용) ──────────────────────────

        /// <summary>
        /// 게임오버 또는 maxPlacements 도달까지 자동 플레이.
        /// 예외 발생 시 즉시 캡처하고 루프를 종료한다 (seed가 리포트에 포함되어 재현 가능).
        /// </summary>
        public BotSessionReport Play(int maxPlacements = 10000)
        {
            var rng     = new System.Random(_seed);
            var board   = new GameControllerBoardState(_game);
            var metrics = new BotMetricsCollector();
            var sw      = new Stopwatch();

            bool reachedGameOver = false;
            int  placements      = 0;

            metrics.StartSession(maxPlacements);

            try
            {
                while (!_game.IsGameOver && placements < maxPlacements)
                {
                    var blocks    = _game.GetAvailableBlocks();
                    var candidate = _strategy.Decide(board, blocks, rng);

                    if (candidate == null) break;

                    var chosenBlock = blocks[candidate.Value.BlockIndex];

                    long allocBefore  = SafeGetTotalAllocated();
                    sw.Restart();
                    bool placed = _game.TryPlaceBlock(chosenBlock, candidate.Value.Position);
                    sw.Stop();
                    long gcAllocDelta = SafeGetTotalAllocated() - allocBefore;

                    if (placed)
                    {
                        placements++;
                        metrics.RecordPlacement(sw.ElapsedMilliseconds, gcAllocDelta, placements);
                    }
                }

                reachedGameOver = _game.IsGameOver;
            }
            catch (Exception ex)
            {
                metrics.RecordException(ex);
            }

            metrics.EndSession(placements);
            return metrics.BuildReport(_seed, _game.GetCurrentScore(), reachedGameOver);
        }

        // ── 실시간 코루틴 플레이 (실기기 Game Loop용) ─────────────────────────

        /// <summary>
        /// 실기기 Game Loop Test용 코루틴. 각 배치 후 1프레임 양보하여
        /// 렌더링/애니메이션이 실제로 처리되도록 한다.
        ///
        /// maxGames 또는 maxDurationSeconds 중 먼저 도달하는 조건까지 반복하고,
        /// 게임오버마다 onGameComplete 콜백으로 세션 리포트를 전달한다.
        ///
        /// MonoBehaviour.StartCoroutine()으로 실행해야 한다.
        /// </summary>
        /// <param name="maxGames">실행할 최대 게임 수</param>
        /// <param name="maxDurationSeconds">총 실행 허용 시간(초). Firebase 무료 쿼터 기준 1500s 권장.</param>
        /// <param name="onGameComplete">게임 1회 완료 시 리포트와 함께 호출되는 콜백</param>
        /// <param name="maxPlacementsPerGame">게임당 최대 배치 횟수 (무한루프 방지)</param>
        public IEnumerator PlayRealtime(
            int                         maxGames,
            float                       maxDurationSeconds,
            Action<BotSessionReport>    onGameComplete,
            int                         maxPlacementsPerGame = 2000,
            Func<IEnumerator>           afterEachGame        = null)
        {
            var board     = new GameControllerBoardState(_game);
            var sw        = new Stopwatch();
            float startAt = Time.realtimeSinceStartup;

            for (int gameIdx = 0; gameIdx < maxGames; gameIdx++)
            {
                if (Time.realtimeSinceStartup - startAt >= maxDurationSeconds) yield break;

                int seed    = _seed + gameIdx * 7919; // 소수 간격으로 결정적 seed
                var rng     = new System.Random(seed);
                var metrics = new BotMetricsCollector();
                int placements = 0;

                metrics.StartSession(maxPlacementsPerGame);

                while (!_game.IsGameOver && placements < maxPlacementsPerGame)
                {
                    if (Time.realtimeSinceStartup - startAt >= maxDurationSeconds) break;

                    Exception caught = null;
                    try
                    {
                        var blocks    = _game.GetAvailableBlocks();
                        var candidate = _strategy.Decide(board, blocks, rng);

                        if (candidate == null) break;

                        var chosenBlock = blocks[candidate.Value.BlockIndex];

                        long allocBefore  = SafeGetTotalAllocated();
                        sw.Restart();
                        bool placed = _game.TryPlaceBlock(chosenBlock, candidate.Value.Position);
                        sw.Stop();
                        long gcAllocDelta = SafeGetTotalAllocated() - allocBefore;

                        if (placed)
                        {
                            placements++;
                            metrics.RecordPlacement(sw.ElapsedMilliseconds, gcAllocDelta, placements);
                        }
                    }
                    catch (Exception ex)
                    {
                        caught = ex;
                        metrics.RecordException(ex);
                    }

                    yield return null; // 1프레임 양보 — 렌더링·애니메이션 처리 허용

                    if (caught != null) break;
                }

                bool reachedGameOver = _game.IsGameOver;
                metrics.EndSession(placements);

                var report = metrics.BuildReport(seed, _game.GetCurrentScore(), reachedGameOver);
                onGameComplete?.Invoke(report);

                if (afterEachGame != null)
                    yield return afterEachGame();

                // 다음 게임 준비: 재시작 후 안착 대기 (마지막 게임 제외)
                bool hasNextGame = gameIdx < maxGames - 1 &&
                                   Time.realtimeSinceStartup - startAt < maxDurationSeconds;
                if (hasNextGame)
                {
                    _game.RestartGame();
                    for (int f = 0; f < 5; f++) yield return null;
                }
            }
        }

        // ── 공통 유틸 ─────────────────────────────────────────────────────────

        private static long SafeGetTotalAllocated() => GC.GetTotalMemory(false);

        // ── IGGameController를 IReadOnlyBoardState로 감싸는 어댑터 ─────────────

        private sealed class GameControllerBoardState : IReadOnlyBoardState
        {
            private readonly IGGameController _game;

            public int Cols => IGConfig.BOARD_COL;
            public int Rows => IGConfig.BOARD_ROW;

            public GameControllerBoardState(IGGameController game) => _game = game;

            public bool CanPlace(IGBlockModel block, Vector2Int position)
                => _game.CanPlaceBlock(block, position);
        }
    }
}
