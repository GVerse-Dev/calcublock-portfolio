using System.Collections.Generic;
using UnityEngine;
using IGMain;

namespace Simulation
{
    /// <summary>
    /// 우선순위: 클리어 발생 위치 > 보드 가장자리 위치 > 아무 빈 곳.
    /// 클리어 위치가 여러 개면 수식 결과(rawScore)가 가장 높은 위치를 선택한다.
    /// </summary>
    public class GreedyStrategy : IBotStrategy
    {
        public Vector2Int? ChoosePlacement(SimBlock block, SimulationBoard board)
        {
            var validPositions = board.GetValidPositions(block);
            if (validPositions.Count == 0) return null;

            // ── Priority 1: 클리어가 발생하는 위치 ──────────────────────────────
            var clearingPositions = new List<Vector2Int>();
            foreach (var pos in validPositions)
                if (board.WouldCauseClear(block, pos))
                    clearingPositions.Add(pos);

            if (clearingPositions.Count > 0)
                return BestByScore(block, board, clearingPositions);

            // ── Priority 2: 보드 가장자리에 맞닿는 위치 ─────────────────────────
            var edgePositions = new List<Vector2Int>();
            foreach (var pos in validPositions)
                if (TouchesEdge(block, pos))
                    edgePositions.Add(pos);

            if (edgePositions.Count > 0)
                return edgePositions[Random.Range(0, edgePositions.Count)];

            // ── Priority 3: 아무 유효 위치 ──────────────────────────────────────
            return validPositions[0];
        }

        // 클리어 위치들 중 수식 결과가 가장 높은 위치를 반환한다.
        private static Vector2Int BestByScore(SimBlock block, SimulationBoard board, List<Vector2Int> candidates)
        {
            Vector2Int best      = candidates[0];
            long       bestScore = long.MinValue;

            foreach (var pos in candidates)
            {
                var clone = board.Clone();
                var (score, _) = clone.Place(block, pos);
                if (score > bestScore)
                {
                    bestScore = score;
                    best      = pos;
                }
            }

            return best;
        }

        // 블록의 어떤 타일이라도 보드 가장자리(x=0, x=8, y=0, y=8)에 놓이면 true.
        private static bool TouchesEdge(SimBlock block, Vector2Int pos)
        {
            int maxX = IGConfig.BOARD_COL - 1;
            int maxY = IGConfig.BOARD_ROW - 1;

            foreach (var o in block.GetRelativeTilePositions())
            {
                int tx = pos.x + o.x;
                int ty = pos.y + o.y;
                if (tx == 0 || tx == maxX || ty == 0 || ty == maxY) return true;
            }
            return false;
        }
    }
}
