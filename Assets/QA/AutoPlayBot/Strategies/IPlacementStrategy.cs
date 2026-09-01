using System.Collections.Generic;
using IGMain;

namespace IGQA.AutoPlayBot.Strategies
{
    /// <summary>
    /// 한 턴의 배치 수(手)를 결정하는 전략 인터페이스.
    /// 구현체는 상태 없이(stateless) 설계해 여러 봇 인스턴스가 공유할 수 있도록 한다.
    /// </summary>
    public interface IPlacementStrategy
    {
        /// <summary>
        /// 현재 보드·블록 상태를 보고 다음에 둘 수를 결정한다.
        /// 유효한 배치가 없으면 null 반환 (봇이 게임오버로 처리).
        /// </summary>
        PlacementCandidate? Decide(
            IReadOnlyBoardState board,
            IReadOnlyList<IGBlockModel> availableBlocks,
            System.Random rng);
    }
}
