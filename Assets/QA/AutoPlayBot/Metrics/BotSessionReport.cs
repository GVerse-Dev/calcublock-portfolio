using System;
using System.Collections.Generic;
using System.Text;

namespace IGQA.AutoPlayBot.Metrics
{
    /// <summary>
    /// 한 게임 세션의 봇 플레이 결과. 불변.
    /// seed가 포함돼 있어 동일 조건으로 재현할 수 있다.
    /// </summary>
    public sealed class BotSessionReport
    {
        public int                        Seed                  { get; }
        public int                        PlacementCount        { get; }
        public long                       FinalScore            { get; }
        public IReadOnlyList<Exception>   Exceptions            { get; }
        public IReadOnlyList<MemorySnapshot> MemorySnapshots    { get; }
        public IReadOnlyList<long>        PlacementDurations    { get; }  // ms
        public IReadOnlyList<long>        PlacementGcAllocBytes { get; }  // bytes per placement
        public bool                       ReachedGameOver       { get; }

        public BotSessionReport(
            int seed,
            int placementCount,
            long finalScore,
            IReadOnlyList<Exception>      exceptions,
            IReadOnlyList<MemorySnapshot> memorySnapshots,
            IReadOnlyList<long>           placementDurations,
            IReadOnlyList<long>           placementGcAllocBytes,
            bool reachedGameOver)
        {
            Seed                  = seed;
            PlacementCount        = placementCount;
            FinalScore            = finalScore;
            Exceptions            = exceptions;
            MemorySnapshots       = memorySnapshots;
            PlacementDurations    = placementDurations;
            PlacementGcAllocBytes = placementGcAllocBytes;
            ReachedGameOver       = reachedGameOver;
        }

        /// <summary>한 줄 요약. TestRunner 로그에 출력용.</summary>
        public string ToSummary()
        {
            long maxMs = 0, totalMs = 0;
            foreach (var d in PlacementDurations) { totalMs += d; if (d > maxMs) maxMs = d; }
            double avgMs = PlacementDurations.Count > 0 ? (double)totalMs / PlacementDurations.Count : 0;

            long startMem = MemorySnapshots.Count > 0 ? MemorySnapshots[0].TotalMemoryBytes : 0;
            long endMem   = MemorySnapshots.Count > 0 ? MemorySnapshots[MemorySnapshots.Count - 1].TotalMemoryBytes : 0;

            var sb = new StringBuilder();
            sb.Append($"[BotReport] seed={Seed}");
            sb.Append($" placements={PlacementCount}");
            sb.Append($" score={FinalScore}");
            sb.Append($" gameOver={ReachedGameOver}");
            sb.Append($" exceptions={Exceptions.Count}");
            sb.Append($" avgMs={avgMs:F2}");
            sb.Append($" maxMs={maxMs}");
            sb.Append($" memStart={startMem / 1048576.0:F1}MB");
            sb.Append($" memEnd={endMem / 1048576.0:F1}MB");
            return sb.ToString();
        }
    }
}
