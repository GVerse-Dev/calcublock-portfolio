using System.IO;
using System.Text;
using UnityEngine;

namespace Simulation
{
    public static class MetricsExporter
    {
        private const string OutputDir = "Assets/SimulationData";

        /// <summary>
        /// metrics 를 두 개의 CSV 파일로 출력한다.
        /// - {prefix}_game_summary.csv  : 1행 = 1게임
        /// - {prefix}_turn_detail.csv   : 1행 = 1턴
        /// </summary>
        public static void Export(SimulationMetrics metrics, string prefix)
        {
            if (!Directory.Exists(OutputDir))
                Directory.CreateDirectory(OutputDir);

            WriteGameSummary(metrics, prefix);
            WriteTurnDetail(metrics, prefix);
        }

        private static void WriteGameSummary(SimulationMetrics metrics, string prefix)
        {
            var sb = new StringBuilder();
            sb.AppendLine(
                "game_id,strategy,total_turns,final_score,max_expression," +
                "min_expression,negative_count,negative_rate," +
                "phase0_turns,phase1_turns,phase2_turns,phase3_turns," +
                "end_board_occupancy");

            foreach (var g in metrics.GameRecords)
            {
                sb.AppendLine(
                    $"{g.GameId},{g.Strategy},{g.TotalTurns},{g.FinalScore}," +
                    $"{g.MaxExpression},{g.MinExpression}," +
                    $"{g.NegativeCount},{g.NegativeRate:F4}," +
                    $"{g.PhaseTurns[0]},{g.PhaseTurns[1]},{g.PhaseTurns[2]},{g.PhaseTurns[3]}," +
                    $"{g.EndBoardOccupancy:F4}");
            }

            File.WriteAllText(Path.Combine(OutputDir, $"{prefix}_game_summary.csv"), sb.ToString());
            Debug.Log($"[MetricsExporter] {prefix}_game_summary.csv 저장 완료 ({metrics.GameRecords.Count}행)");
        }

        private static void WriteTurnDetail(SimulationMetrics metrics, string prefix)
        {
            var sb = new StringBuilder();
            sb.AppendLine(
                "game_id,turn,phase,clear_count,board_occupancy," +
                "expression_result,combo_multiplier," +
                "num_tiles,op_tiles,blank_tiles");

            foreach (var t in metrics.TurnRecords)
            {
                sb.AppendLine(
                    $"{t.GameId},{t.Turn},{t.Phase},{t.ClearCount},{t.BoardOccupancy:F4}," +
                    $"{t.ExpressionResult},{t.ComboMultiplier:F2}," +
                    $"{t.NumTiles},{t.OpTiles},{t.BlankTiles}");
            }

            File.WriteAllText(Path.Combine(OutputDir, $"{prefix}_turn_detail.csv"), sb.ToString());
            Debug.Log($"[MetricsExporter] {prefix}_turn_detail.csv 저장 완료 ({metrics.TurnRecords.Count}행)");
        }
    }
}
