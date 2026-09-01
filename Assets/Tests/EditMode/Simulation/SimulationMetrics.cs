using System.Collections.Generic;

namespace Simulation
{
    public class TurnRecord
    {
        public int   GameId;
        public int   Turn;
        public int   Phase;
        public int   ClearCount;
        public float BoardOccupancy;
        public long  ExpressionResult;  // 이번 배치의 raw score (클리어 없으면 0)
        public float ComboMultiplier;   // 1.0 + ComboCount * 0.1
        public int   NumTiles;          // 블록의 채워진 타일 수
        public int   OpTiles;           // +, -, *, / 타일 수
        public int   BlankTiles;        // 공백(' ') 타일 수
    }

    public class GameRecord
    {
        public int     GameId;
        public string  Strategy;
        public int     TotalTurns;
        public long    FinalScore;
        public long    MaxExpression;
        public long    MinExpression;
        public int     NegativeCount;
        public float   NegativeRate;
        public int[]   PhaseTurns = new int[4]; // [phase0, phase1, phase2, phase3]
        public float   EndBoardOccupancy;
    }

    public class SimulationMetrics
    {
        public readonly List<TurnRecord> TurnRecords = new();
        public readonly List<GameRecord> GameRecords = new();
    }
}
