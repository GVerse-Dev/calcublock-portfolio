using IGMain;
using UnityEngine;

namespace IGQA.AutoPlayBot
{
    /// <summary>
    /// 전략이 보드 상태를 조회할 수 있는 읽기 전용 인터페이스.
    /// 내부 보드 데이터를 노출하지 않고 배치 가능 여부만 질의할 수 있도록 한다.
    /// </summary>
    public interface IReadOnlyBoardState
    {
        int Cols { get; }
        int Rows { get; }

        /// <summary>주어진 그리드 위치에 블록을 배치할 수 있으면 true.</summary>
        bool CanPlace(IGBlockModel block, Vector2Int position);
    }
}
