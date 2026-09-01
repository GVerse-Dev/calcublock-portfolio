namespace IGMain
{
    public interface IPhaseDataProvider
    {
        /// <summary>라인 + 스퀘어 클리어 총합 (단방향 증가, 페이즈 결정 주 변수)</summary>
        int TotalClearCount { get; }

        /// <summary>현재 채워진 칸 / 81 (보정 보조 변수, 0..1)</summary>
        float BoardOccupancyRatio { get; }
    }
}
