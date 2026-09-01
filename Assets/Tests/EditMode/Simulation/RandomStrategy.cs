using UnityEngine;

namespace Simulation
{
    /// <summary>
    /// 배치 가능한 위치 중 하나를 랜덤하게 선택한다.
    /// GreedyStrategy 와 비교하여 "봇 실력 vs 시스템 설계" 를 구분하는 대조군.
    /// </summary>
    public class RandomStrategy : IBotStrategy
    {
        public Vector2Int? ChoosePlacement(SimBlock block, SimulationBoard board)
        {
            var valid = board.GetValidPositions(block);
            if (valid.Count == 0) return null;
            return valid[Random.Range(0, valid.Count)];
        }
    }
}
