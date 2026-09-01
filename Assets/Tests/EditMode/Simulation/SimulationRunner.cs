using System.Collections.Generic;
using UnityEngine;
using IGMain;

namespace Simulation
{
    /// <summary>
    /// N판을 동기 루프로 시뮬레이션한다.
    /// 렌더링·MonoBehaviour·코루틴 없이 Edit Mode 에서 수 초 내 완료된다.
    /// </summary>
    public class SimulationRunner
    {
        private readonly int                gameCount;
        private readonly IBotStrategy       strategy;
        private readonly TilePhaseProfile[] profiles;
        private readonly int                seed;

        public SimulationRunner(int gameCount, IBotStrategy strategy,
                                TilePhaseProfile[] profiles, int seed)
        {
            this.gameCount = gameCount;
            this.strategy  = strategy;
            this.profiles  = profiles;
            this.seed      = seed;
        }

        public SimulationMetrics Execute()
        {
            var metrics = new SimulationMetrics();

            // 전체 시뮬레이션의 재현성을 위해 전역 시드를 한 번 설정한다.
            Random.InitState(seed);

            var board = new SimulationBoard();

            for (int gameId = 0; gameId < gameCount; gameId++)
            {
                board.Reset();

                var generator  = new TileValueGenerator(board, profiles);
                var gameRecord = new GameRecord
                {
                    GameId   = gameId,
                    Strategy = strategy.GetType().Name,
                };

                long turnScore   = 0;
                int  comboCount  = 0;
                int  turn        = 0;
                long maxExpr     = long.MinValue;
                long minExpr     = long.MaxValue;
                int  negCount    = 0;
                int  exprCount   = 0;

                bool gameOver = false;

                while (!gameOver)
                {
                    // ── 블록 세트 3개 생성 ──────────────────────────────────────
                    var blockSet = GenerateBlockSet(generator);

                    // ── 게임오버 사전 체크 ──────────────────────────────────────
                    if (!board.IsAnyPlaceable(blockSet))
                    {
                        gameOver = true;
                        break;
                    }

                    // ── 각 블록 배치 ────────────────────────────────────────────
                    foreach (var block in blockSet)
                    {
                        var pos = strategy.ChoosePlacement(block, board);
                        if (pos == null)
                        {
                            gameOver = true;
                            break;
                        }

                        var (rawScore, clearedCount) = board.Place(block, pos.Value);

                        // 현재 페이즈 (TileProbabilityResolver.Resolve 와 동일한 기준)
                        int phase = ResolvePhaseIndex(board.TotalClearCount, profiles);

                        // 점수 / 콤보 (연속 클리어마다 +0.1배, 최대 2.0배 — 실게임 IGScoreController와 동일)
                        long finalScore    = 0;
                        float comboMult    = Mathf.Min(2.0f, 1f + comboCount * 0.1f);
                        bool  didClear     = clearedCount > 0;

                        if (didClear)
                        {
                            finalScore  = (long)(rawScore * comboMult);
                            turnScore  += finalScore;
                            comboCount++;

                            // 표현식 통계 (rawScore 기준)
                            if (rawScore > maxExpr) maxExpr = rawScore;
                            if (rawScore < minExpr) minExpr = rawScore;
                            if (rawScore < 0) negCount++;
                            exprCount++;
                        }
                        else
                        {
                            comboCount = 0;
                        }

                        // ── 타일 종류 집계 ──────────────────────────────────────
                        int numTiles = 0, opTiles = 0, blankTiles = 0;
                        foreach (var v in block.FilledValues())
                        {
                            numTiles++;
                            if (v == "+" || v == "-" || v == "*" || v == "/") opTiles++;
                            else if (v == " " || v == "")                      blankTiles++;
                        }

                        gameRecord.PhaseTurns[phase]++;
                        turn++;

                        metrics.TurnRecords.Add(new TurnRecord
                        {
                            GameId           = gameId,
                            Turn             = turn,
                            Phase            = phase,
                            ClearCount       = clearedCount,
                            BoardOccupancy   = board.BoardOccupancyRatio,
                            ExpressionResult = didClear ? rawScore : 0,
                            ComboMultiplier  = didClear ? comboMult : 1f,
                            NumTiles         = numTiles,
                            OpTiles          = opTiles,
                            BlankTiles       = blankTiles,
                        });
                    }
                }

                // ── 게임 결과 기록 ──────────────────────────────────────────────
                if (exprCount == 0) { maxExpr = 0; minExpr = 0; }

                gameRecord.TotalTurns        = turn;
                gameRecord.FinalScore        = turnScore;
                gameRecord.MaxExpression     = maxExpr;
                gameRecord.MinExpression     = minExpr;
                gameRecord.NegativeCount     = negCount;
                gameRecord.NegativeRate      = exprCount > 0 ? (float)negCount / exprCount : 0f;
                gameRecord.EndBoardOccupancy = board.BoardOccupancyRatio;

                metrics.GameRecords.Add(gameRecord);
            }

            return metrics;
        }

        // ── 내부 헬퍼 ─────────────────────────────────────────────────────────────

        private List<SimBlock> GenerateBlockSet(TileValueGenerator generator)
        {
            var blocks       = new List<SimBlock>(3);
            var allTileInfos = new List<(int bi, int y, int x)>(); // for "ensure number" check
            bool hasNumber   = false;

            for (int i = 0; i < 3; i++)
            {
                var shape  = new BlockShape();
                var values = new string[shape.Height, shape.Width];

                for (int y = 0; y < shape.Height; y++)
                {
                    for (int x = 0; x < shape.Width; x++)
                    {
                        if (shape.Shape[y, x] != 1) continue;

                        var v = generator.GetValue();
                        values[y, x] = v;

                        if (!hasNumber && generator.IsNumber(v)) hasNumber = true;
                        allTileInfos.Add((i, y, x));
                    }
                }

                blocks.Add(new SimBlock(shape, values));
            }

            // 세트 내 숫자(1-9) 타일이 없으면 하나 강제 삽입
            if (!hasNumber && allTileInfos.Count > 0)
            {
                var pick = allTileInfos[Random.Range(0, allTileInfos.Count)];
                // 해당 블록의 values 배열에 접근하기 위해 재생성은 불가 → SimBlock 을 교체
                var oldBlock = blocks[pick.bi];
                var newValues = CopyValues(oldBlock.Shape.Height, oldBlock.Shape.Width, oldBlock);
                newValues[pick.y, pick.x] = generator.GetNumber();
                blocks[pick.bi] = new SimBlock(oldBlock.Shape, newValues);
            }

            return blocks;
        }

        private static string[,] CopyValues(int h, int w, SimBlock block)
        {
            var arr = new string[h, w];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var td = block.GetTileData(
                        x - Mathf.RoundToInt(block.Shape.VisualPivot.x),
                        y - Mathf.RoundToInt(block.Shape.VisualPivot.y));
                    arr[y, x] = td.IsValid ? td.Value : null;
                }
            return arr;
        }

        // TilePhaseProfile.ClearThreshold 를 기반으로 현재 페이즈 인덱스를 반환한다.
        private static int ResolvePhaseIndex(int totalClear, TilePhaseProfile[] profiles)
        {
            int phase = 0;
            for (int i = 0; i < profiles.Length; i++)
                if (totalClear >= profiles[i].ClearThreshold) phase = i;
            return phase;
        }
    }
}
