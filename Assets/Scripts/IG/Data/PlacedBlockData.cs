using UnityEngine;

namespace IGMain
{
    /// <summary>
    /// 보드에 배치된 블록의 데이터를 저장하는 구조체
    /// 블록 클래스 전체가 아닌 필요한 데이터만 보드 타일에 저장
    /// </summary>
    [System.Serializable]
    public struct PlacedBlockData
    {
        /// <summary>
        /// 계산식 (예: "3+5", "7*2")
        /// </summary>
        public string Formula;

        /// <summary>
        /// 타일 색상
        /// </summary>
        public Color TileColor;

        /// <summary>
        /// 블록 타입 ID (통계/분석용)
        /// </summary>
        public int BlockTypeId;

        /// <summary>
        /// 생성자
        /// </summary>
        public PlacedBlockData(string formula, Color color, int typeId)
        {
            Formula = formula;
            TileColor = color;
            BlockTypeId = typeId;
        }

        /// <summary>
        /// 빈 데이터 (기본값)
        /// </summary>
        public static PlacedBlockData Empty => new PlacedBlockData("", Color.white, -1);

        /// <summary>
        /// 유효한 데이터인지 확인
        /// </summary>
        public bool IsValid => BlockTypeId >= 0;
    }
}
