using System.IO;
using NUnit.Framework;
using IGMain;
using Simulation;

/// <summary>
/// 레벨 디자인 튜닝용 오토봇 시뮬레이터.
/// Unity Test Runner (Edit Mode) 에서 실행한다.
///
/// 사용법:
///   1. TilePhaseProfile.BuildProfiles() 가중치를 조정한다.
///   2. 동일한 seed 로 테스트를 실행한다.
///   3. Assets/SimulationData/ 의 CSV 를 비교한다.
/// </summary>
[TestFixture]
public class LevelDesignSimulation
{
    [Test]
    public void Run_Greedy_1000Games()
    {
        var runner = new SimulationRunner(
            gameCount : 1000,
            strategy  : new GreedyStrategy(),
            profiles  : PhaseProfiles.Default,
            seed      : 42
        );

        var metrics = runner.Execute();
        MetricsExporter.Export(metrics, "greedy_1000");

        Assert.IsTrue(File.Exists("Assets/SimulationData/greedy_1000_game_summary.csv"),
                      "game_summary CSV 가 생성되지 않았습니다.");
        Assert.IsTrue(File.Exists("Assets/SimulationData/greedy_1000_turn_detail.csv"),
                      "turn_detail CSV 가 생성되지 않았습니다.");

        int games = metrics.GameRecords.Count;
        int turns = metrics.TurnRecords.Count;
        UnityEngine.Debug.Log($"[Greedy] {games}판 완료, 총 {turns}턴");
    }

    [Test]
    public void Run_Random_1000Games()
    {
        var runner = new SimulationRunner(
            gameCount : 1000,
            strategy  : new RandomStrategy(),
            profiles  : PhaseProfiles.Default,
            seed      : 42
        );

        var metrics = runner.Execute();
        MetricsExporter.Export(metrics, "random_1000");

        Assert.IsTrue(File.Exists("Assets/SimulationData/random_1000_game_summary.csv"),
                      "game_summary CSV 가 생성되지 않았습니다.");
        Assert.IsTrue(File.Exists("Assets/SimulationData/random_1000_turn_detail.csv"),
                      "turn_detail CSV 가 생성되지 않았습니다.");

        int games = metrics.GameRecords.Count;
        int turns = metrics.TurnRecords.Count;
        UnityEngine.Debug.Log($"[Random] {games}판 완료, 총 {turns}턴");
    }
}

/// <summary>TilePhaseProfile 기본 프로필 접근용 헬퍼.</summary>
public static class PhaseProfiles
{
    public static TilePhaseProfile[] Default => TilePhaseProfile.BuildProfiles();
}
