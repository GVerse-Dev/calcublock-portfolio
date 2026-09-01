namespace IGQA.AutoPlayBot.Metrics
{
    /// <summary>
    /// 특정 배치 시점의 메모리 상태 스냅샷. 불변.
    /// PlacementIndex = -1 은 세션 시작(배치 전), 양수는 해당 배치 직후를 의미한다.
    /// </summary>
    public readonly struct MemorySnapshot
    {
        public readonly int  PlacementIndex;
        public readonly long TotalMemoryBytes;
        public readonly long TotalAllocatedBytes; // GC.GetTotalAllocatedBytes 미지원 시 0

        public MemorySnapshot(int placementIndex, long totalMemoryBytes, long totalAllocatedBytes)
        {
            PlacementIndex       = placementIndex;
            TotalMemoryBytes     = totalMemoryBytes;
            TotalAllocatedBytes  = totalAllocatedBytes;
        }
    }
}
