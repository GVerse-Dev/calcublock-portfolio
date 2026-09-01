using System;
using System.Collections.Generic;

namespace IGQA.AutoPlayBot.Metrics
{
    /// <summary>
    /// 봇 플레이 중 메트릭을 수집하고 최종 리포트를 생성하는 도우미 클래스.
    /// AutoPlayBot 내부에서만 사용한다.
    /// </summary>
    internal sealed class BotMetricsCollector
    {
        private readonly List<Exception>      _exceptions         = new List<Exception>();
        private readonly List<MemorySnapshot> _memSnapshots       = new List<MemorySnapshot>();
        private readonly List<long>           _placementDurations = new List<long>();
        private readonly List<long>           _placementGcAllocs  = new List<long>();

        private int _snapshotInterval;
        private int _placementCount;

        // ── 세션 시작/종료 ────────────────────────────────────────────────────

        /// <summary>
        /// 루프 시작 전 호출. 초기 스냅샷을 기록하고 스냅샷 간격을 결정한다.
        /// maxPlacements ≤ 0 이면 기본 간격(50)을 사용한다.
        /// </summary>
        public void StartSession(int maxPlacements)
        {
            _snapshotInterval = maxPlacements > 0 ? Math.Max(1, maxPlacements / 10) : 50;
            _memSnapshots.Add(TakeSnapshot(-1));
        }

        /// <summary>루프 종료 후 호출. 최종 스냅샷을 기록해 ≥2 개를 보장한다.</summary>
        public void EndSession(int totalPlacements)
        {
            _memSnapshots.Add(TakeSnapshot(totalPlacements));
        }

        // ── 배치 기록 ─────────────────────────────────────────────────────────

        /// <summary>블록 배치 1회 성공 시 호출.</summary>
        /// <param name="durationMs">배치 소요 시간(ms)</param>
        /// <param name="gcAllocDelta">배치 전후 GC 할당량 증분(bytes). 불지원 시 0.</param>
        /// <param name="placementIndex">현재 배치 순번(1-based)</param>
        public void RecordPlacement(long durationMs, long gcAllocDelta, int placementIndex)
        {
            _placementCount++;
            _placementDurations.Add(durationMs);
            _placementGcAllocs.Add(gcAllocDelta);

            if (_placementCount % _snapshotInterval == 0)
                _memSnapshots.Add(TakeSnapshot(placementIndex));
        }

        /// <summary>예외 발생 시 호출.</summary>
        public void RecordException(Exception ex) => _exceptions.Add(ex);

        // ── 리포트 생성 ───────────────────────────────────────────────────────

        public BotSessionReport BuildReport(int seed, long finalScore, bool reachedGameOver)
            => new BotSessionReport(
                seed,
                _placementCount,
                finalScore,
                _exceptions.AsReadOnly(),
                _memSnapshots.AsReadOnly(),
                _placementDurations.AsReadOnly(),
                _placementGcAllocs.AsReadOnly(),
                reachedGameOver);

        // ── 내부 유틸 ─────────────────────────────────────────────────────────

        private static MemorySnapshot TakeSnapshot(int placementIndex)
        {
            long totalMem   = GC.GetTotalMemory(false);
            long totalAlloc = SafeGetTotalAllocated();
            return new MemorySnapshot(placementIndex, totalMem, totalAlloc);
        }

        private static long SafeGetTotalAllocated() => GC.GetTotalMemory(false);
    }
}
