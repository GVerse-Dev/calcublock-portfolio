using System;
using UniRx;

namespace IGMain.Ads
{
    /// <summary>
    /// 광고 SDK 콜백을 Unity 메인 스레드로 넘긴다.
    ///
    /// GMA/UMP 콜백은 Java 백그라운드 스레드에서 올라온다.
    /// 그 스레드에서 Unity API를 만지면 예외가 나고, 콜백에 물려 있던
    /// 게임 로직이 통째로 죽는다. (실기기에서 두 번 겪음:
    /// 동의 수집 흐름 중단, 보상형 광고 시청 후 부활 실패)
    ///
    /// GMA의 MobileAdsEventExecutor는 쓰지 않는다.
    /// 그건 MobileAds.Initialize() 과정에서 생성되므로 초기화 이전 단계인
    /// 동의 수집에서는 콜백이 영영 실행되지 않는다.
    /// UniRx 디스패처는 GMA와 무관하게 동작한다.
    /// </summary>
    internal static class AdMainThread
    {
        /// <summary>
        /// 메인 스레드에서 실행되도록 예약한다.
        /// 예약 순서는 보존되므로, 보상 적립 콜백과 광고 종료 콜백의 선후 관계도 유지된다.
        /// </summary>
        public static void Run(Action action)
        {
            if (action == null) return;

            MainThreadDispatcher.Post(_ => action(), null);
        }

        /// <summary>
        /// 메인 스레드 디스패처를 미리 준비한다.
        /// 반드시 메인 스레드에서 한 번 호출해 둘 것.
        /// </summary>
        public static void EnsureInitialized()
        {
            MainThreadDispatcher.Initialize();
        }
    }
}
