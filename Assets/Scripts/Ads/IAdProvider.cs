using System;

namespace IGMain.Ads
{
    /// <summary>
    /// 광고 SDK 추상화 인터페이스.
    /// 게임 코드는 이 인터페이스만 호출 — 어떤 SDK 인지 모름.
    /// 
    /// 구현체:
    /// - AdMobProvider (현재)
    /// - 향후: UnityAdsProvider, LevelPlayProvider 등 추가 가능
    /// </summary>
    public interface IAdProvider
    {
        /// <summary>SDK 초기화 + 첫 광고 로드 시작.</summary>
        void Initialize();

        // ── 전면 광고 (Interstitial) ─────────────────
        /// <summary>전면 광고 미리 로드.</summary>
        void LoadInterstitial();

        /// <summary>
        /// 전면 광고 표시. 광고가 끝나면(성공·실패 무관) onClosed 호출.
        ///
        /// onClosed(shown) 의 shown 은 **광고가 실제로 화면에 노출된 뒤 닫혔는지**를 뜻한다.
        /// 노출 실패(OnAdFullScreenContentFailed), 미동의, 광고 미준비 같은 경로는 게임 흐름을
        /// 막지 않기 위해 콜백을 즉시 돌려주지만 shown=false 다.
        ///
        /// 이 구분이 필요한 이유: 호출자(AdGatePolicy)가 노출 횟수를 디스크에 영속화하므로,
        /// 안 띄운 것을 띄웠다고 기록하면 오염이 세션을 넘어 남는다. AdManager 참고.
        /// </summary>
        void ShowInterstitial(Action<bool> onClosed = null);

        /// <summary>전면 광고 재생 준비 됐는지.</summary>
        bool IsInterstitialReady { get; }

        // ── 보상형 광고 (Rewarded) ───────────────────
        /// <summary>보상형 광고 미리 로드.</summary>
        void LoadRewarded();

        /// <summary>보상형 광고 표시. onResult(true) = 시청 완료, onResult(false) = 중도 종료.</summary>
        void ShowRewarded(Action<bool> onResult);

        /// <summary>보상형 광고 재생 준비 됐는지.</summary>
        bool IsRewardedReady { get; }

        /// <summary>
        /// 로드해 둔 광고를 전부 파기한다. 사용자가 세션 중 동의를 철회했을 때 호출한다.
        ///
        /// 이것이 없으면 철회 이전에 로드된 광고가 그대로 남아 그 세션 동안 계속 노출된다.
        /// </summary>
        void DiscardLoadedAds();
    }
}