using IGQA.AutoPlayBot.Metrics;

namespace IGQA.AutoPlayBot
{
    public interface IAutoPlayBot
    {
        /// <summary>게임 오버 또는 maxPlacements 도달까지 자동 플레이하고 세션 리포트를 반환.</summary>
        BotSessionReport Play(int maxPlacements = 10000);
    }
}
