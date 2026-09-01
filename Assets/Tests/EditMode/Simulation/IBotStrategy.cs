using UnityEngine;

namespace Simulation
{
    public interface IBotStrategy
    {
        /// <summary>
        /// block 을 배치할 위치를 결정한다.
        /// 배치 가능한 위치가 없으면 null 을 반환한다.
        /// </summary>
        Vector2Int? ChoosePlacement(SimBlock block, SimulationBoard board);
    }
}
