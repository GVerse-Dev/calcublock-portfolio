using UnityEngine;

namespace IGQA.AutoPlayBot
{
    /// <summary>
    /// 봇이 한 턴에 선택한 배치 수(手).
    /// 불변 struct — GC 부담 없이 대량 후보 목록을 다룰 수 있다.
    /// </summary>
    public readonly struct PlacementCandidate
    {
        /// <summary>GetAvailableBlocks() 반환 목록 내 블록 인덱스.</summary>
        public readonly int BlockIndex;

        /// <summary>배치할 보드 그리드 좌표 (x: 열, y: 행).</summary>
        public readonly Vector2Int Position;

        public PlacementCandidate(int blockIndex, Vector2Int position)
        {
            BlockIndex = blockIndex;
            Position   = position;
        }

        public override string ToString() =>
            $"[block={BlockIndex} pos=({Position.x},{Position.y})]";
    }
}
