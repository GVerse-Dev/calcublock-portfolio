using System.Collections.Generic;
using IGMain;
using UnityEngine;

namespace IGQA.AutoPlayBot.Strategies
{
    /// <summary>
    /// 무작위 배치 전략.
    /// 현재 블록 세트 × 보드 전체 위치 조합 중 유효한 것을 모두 열거한 뒤
    /// RNG로 하나를 선택한다.
    /// 유효한 배치가 없으면 null을 반환한다 (게임오버 신호).
    /// </summary>
    public sealed class RandomPlacementStrategy : IPlacementStrategy
    {
        // 후보 목록 재사용 — 매 턴 GC 압력 최소화
        private readonly List<PlacementCandidate> _candidates = new List<PlacementCandidate>(256);

        public PlacementCandidate? Decide(
            IReadOnlyBoardState board,
            IReadOnlyList<IGBlockModel> availableBlocks,
            System.Random rng)
        {
            _candidates.Clear();

            for (int i = 0; i < availableBlocks.Count; i++)
            {
                var block = availableBlocks[i];
                if (block == null) continue;

                for (int y = 0; y < board.Rows; y++)
                {
                    for (int x = 0; x < board.Cols; x++)
                    {
                        var pos = new Vector2Int(x, y);
                        if (board.CanPlace(block, pos))
                            _candidates.Add(new PlacementCandidate(i, pos));
                    }
                }
            }

            if (_candidates.Count == 0) return null;

            return _candidates[rng.Next(_candidates.Count)];
        }
    }
}
