using System;
using GoogleMobileAds.Api;
using UnityEngine;

namespace IGMain.Ads
{
    /// <summary>
    /// Google AdMob SDK 구현.
    /// 
    /// 책임:
    /// - SDK 초기화
    /// - 광고 로드 + 표시
    /// - 자동 재로드 (한 번 보고 나면 다음 광고 미리 준비)
    /// - 콜백 메모리 관리
    /// </summary>
    public class AdMobProvider : IAdProvider
    {
        // ── 광고 인스턴스 ─────────────────────────────
        private InterstitialAd _interstitialAd;
        private RewardedAd _rewardedAd;

        // ── 상태 노출 ─────────────────────────────────
        public bool IsInterstitialReady => _interstitialAd != null && _interstitialAd.CanShowAd();
        public bool IsRewardedReady => _rewardedAd != null && _rewardedAd.CanShowAd();

        // ── 콜백 보관 (광고 닫힌 후 호출용) ───────────
        // 전면 콜백의 bool 인자 = "실제로 노출된 뒤 닫혔는가". 노출 실패·미동의·미준비는 false.
        // IAdProvider.ShowInterstitial 주석 참고.
        private Action<bool> _onInterstitialClosed;
        private Action<bool> _onRewardedResult;

        // ── 보상 받았는지 추적 (Rewarded 광고용) ──────
        private bool _rewardEarned;

        // ────────────────────────────────────────────
        // Initialize
        // ────────────────────────────────────────────
        public void Initialize()
        {
            AdMainThread.EnsureInitialized();

            // 광고 콘텐츠 등급 상한. **Initialize보다 먼저** 호출해야 첫 광고 요청부터 적용된다.
            //
            // 이 앱의 Play 스토어 등급은 "전체이용가"인데, 이 설정이 없으면 AdMob은 상한 없이
            // 광고를 채운다. 전체이용가 게임에 청소년·성인 등급 광고가 나가는 상태가 된다.
            // G로 두어 스토어 등급과 일치시킨다. 재고가 줄어 fill·eCPM이 낮아질 수 있지만,
            // 등급 불일치를 감수할 이유가 없다.
            //
            // 주의: 아동 대상 앱이 아니므로 TagForChildDirectedTreatment는 설정하지 않는다.
            // (설정하면 COPPA 취급이 되어 개인 맞춤 광고가 완전히 차단된다)
            // UMP 쪽 TagForUnderAgeOfConsent는 동의 수집용이라 별개다. ConsentManager 참고.
            MobileAds.SetRequestConfiguration(new RequestConfiguration
            {
                MaxAdContentRating = MaxAdContentRating.G,
            });

            // AdMob SDK 초기화 (비동기)
            // 이하 모든 SDK 콜백은 Java 백그라운드 스레드에서 올라오므로
            // 게임 코드를 건드리기 전에 반드시 메인 스레드로 넘긴다. AdMainThread 참고.
            MobileAds.Initialize(initStatus =>
            {
                AdMainThread.Run(() =>
                {
#if UNITY_EDITOR
                    Debug.Log($"<color=cyan>AdMob initialized. Status: {initStatus}</color>");
#endif
                    // 초기화 끝나면 첫 광고 로드
                    LoadInterstitial();
                    LoadRewarded();
                });
            });
        }

        // ════════════════════════════════════════════
        // Interstitial (전면 광고)
        // ════════════════════════════════════════════

        public void LoadInterstitial()
        {
            // 기존 광고 정리 (중복 방지)
            if (_interstitialAd != null)
            {
                _interstitialAd.Destroy();
                _interstitialAd = null;
            }

            // 동의가 없으면 요청 자체를 하지 않는다. 이 경로는 광고가 닫힐 때마다 자동
            // 재로드로도 들어오므로, 세션 중 철회했을 때 여기서 막지 않으면 계속 요청이 나간다.
            if (!ConsentManager.CanRequestAds)
            {
                // 이 가드가 조용히 걸리면 광고 로그가 통째로 사라져 "왜 광고가 안 뜨지"를
                // 추적할 단서가 없어진다. 실제로 그것 때문에 진단이 어려웠으므로 흔적을 남긴다.
                // DEBUG_ADS 빌드에서만 컴파일되어 스토어 빌드에는 들어가지 않는다.
#if DEBUG_ADS
                Debug.Log("[Ads] Interstitial load skipped — 동의 없음 (CanRequestAds=false)");
#endif
                return;
            }

            var adRequest = new AdRequest();
            string adUnitId = AdUnitIds.Interstitial;

            InterstitialAd.Load(adUnitId, adRequest, (ad, error) =>
            {
                AdMainThread.Run(() =>
                {
                    if (error != null || ad == null)
                    {
                        Debug.LogWarning($"[Ads] Interstitial load failed: {error?.GetMessage()}");
                        return;
                    }

                    _interstitialAd = ad;
                    RegisterInterstitialCallbacks(ad);

                    Debug.Log("[Ads] Interstitial loaded and ready.");
                });
            });
        }

        private void RegisterInterstitialCallbacks(InterstitialAd ad)
        {
            // 광고가 전체 화면을 가렸을 때
            ad.OnAdFullScreenContentOpened += () =>
            {
#if UNITY_EDITOR
                Debug.Log("Interstitial opened");
#endif
            };

            // 광고가 닫혔을 때 = 실제로 노출된 뒤 닫힌 유일한 경로. 여기서만 shown=true 다.
            ad.OnAdFullScreenContentClosed += () => AdMainThread.Run(() =>
            {
                Debug.Log("[Ads] Interstitial closed");

                // 콜백을 먼저 떼어낸 뒤 부른다. 아래 finally 때문에 Invoke가 던져도 재로드가
                // 돌아가므로, 필드를 남겨 두면 그 다음 광고의 닫힘에서 죽은 콜백이 한 번 더
                // 불려 게임 흐름이 이중으로 진행된다.
                var callback = _onInterstitialClosed;
                _onInterstitialClosed = null;

                // 게임 콜백이 던져도 재로드는 반드시 한다. 이 Invoke는 게임 코드(팝업·세션 저장)
                // 까지 이어지므로 예외가 나갈 수 있고, 그러면 앱 재시작 전까지 전면 광고가 죽는다.
                try
                {
                    callback?.Invoke(true);
                }
                finally
                {
                    // 다음 광고 미리 로드 (자동 재로드 패턴)
                    LoadInterstitial();
                }
            });

            // 광고 표시 실패 시. 노출은 없었으므로 shown=false —
            // 이걸 true로 넘기면 게이트가 리셋되어 광고를 못 띄운 게임오버가 소비된다.
            ad.OnAdFullScreenContentFailed += (AdError error) => AdMainThread.Run(() =>
            {
                Debug.LogWarning($"[Ads] Interstitial show failed: {error.GetMessage()}");

                var callback = _onInterstitialClosed;
                _onInterstitialClosed = null;

                // 실패해도 콜백 호출 (게임 흐름 막히지 않게).
                // 재로드는 finally — 위 닫힘 경로와 같은 이유.
                try
                {
                    callback?.Invoke(false);
                }
                finally
                {
                    LoadInterstitial();
                }
            });
        }

        public void ShowInterstitial(Action<bool> onClosed = null)
        {
            _onInterstitialClosed = onClosed;

            // 세션 중 철회했다면 이미 로드된 광고라도 노출하지 않는다.
            // 게임 흐름은 막지 않는다 — 광고가 없을 때와 동일하게 콜백을 즉시 돌려준다.
            // 노출은 없었으므로 shown=false.
            if (!ConsentManager.CanRequestAds)
            {
                DiscardLoadedAds();

                var denied = _onInterstitialClosed;
                _onInterstitialClosed = null;
                denied?.Invoke(false);
                return;
            }

            if (_interstitialAd != null && _interstitialAd.CanShowAd())
            {
                // 노출 결과는 여기서 판정하지 않는다. Show()는 성공을 보장하지 않으므로
                // OnAdFullScreenContentClosed / Failed 콜백이 shown 값을 정한다.
                _interstitialAd.Show();
            }
            else
            {
                // 광고 없으면 콜백 즉시 호출 (게임 흐름 보장). 노출 없음 → shown=false.
#if UNITY_EDITOR
                Debug.LogWarning("Interstitial not ready. Skipping.");
#endif
                var callback = _onInterstitialClosed;
                _onInterstitialClosed = null;

                // 게임 콜백이 던져도 다음 기회를 위한 로드는 반드시 시도한다.
                try
                {
                    callback?.Invoke(false);
                }
                finally
                {
                    LoadInterstitial();
                }
            }
        }

        // ════════════════════════════════════════════
        // Rewarded (보상형 광고)
        // ════════════════════════════════════════════

        public void LoadRewarded()
        {
            if (_rewardedAd != null)
            {
                _rewardedAd.Destroy();
                _rewardedAd = null;
            }

            // LoadInterstitial과 같은 이유의 가드. 자동 재로드 경로를 함께 막는다.
            if (!ConsentManager.CanRequestAds)
            {
#if DEBUG_ADS
                Debug.Log("[Ads] Rewarded load skipped — 동의 없음 (CanRequestAds=false)");
#endif
                return;
            }

            var adRequest = new AdRequest();
            string adUnitId = AdUnitIds.Rewarded;

            RewardedAd.Load(adUnitId, adRequest, (ad, error) =>
            {
                AdMainThread.Run(() =>
                {
                    if (error != null || ad == null)
                    {
                        Debug.LogWarning($"[Ads] Rewarded load failed: {error?.GetMessage()}");
                        return;
                    }

                    _rewardedAd = ad;
                    RegisterRewardedCallbacks(ad);

                    Debug.Log("[Ads] Rewarded loaded and ready.");
                });
            });
        }

        private void RegisterRewardedCallbacks(RewardedAd ad)
        {
            // 광고 닫힘 (시청 완료 또는 중도 종료)
            ad.OnAdFullScreenContentClosed += () => AdMainThread.Run(() =>
            {
                Debug.Log($"[Ads] Rewarded closed. Reward earned: {_rewardEarned}");

                // 상태를 먼저 비우고 콜백을 부른다. 아래 finally 덕에 Invoke가 던져도 재로드가
                // 돌아가므로, 여기서 안 비우면 다음 광고의 닫힘에서 죽은 콜백이 다시 불리거나
                // 시청하지도 않은 보상(_rewardEarned=true)이 그대로 넘어간다.
                var callback = _onRewardedResult;
                bool earned  = _rewardEarned;
                _onRewardedResult = null;
                _rewardEarned = false;

                // 이 Invoke 체인은 GameOverPopup → RequestRevive → ReviveGame → 세션 저장까지
                // 이어진다. 거기서 예외가 나가면 아래 재로드가 스킵되어 앱 재시작 전까지
                // 부활 광고가 죽는다(= 유일한 수익원 손실). 그래서 재로드는 finally 에 둔다.
                // SaveManager 내부 try/catch 는 직렬화·쓰기만 덮으므로 여기서 한 번 더 막는다.
                try
                {
                    // 보상 받았는지 여부로 결과 전달
                    callback?.Invoke(earned);
                }
                finally
                {
                    // 다음 광고 미리 로드
                    LoadRewarded();
                }
            });

            ad.OnAdFullScreenContentFailed += (AdError error) => AdMainThread.Run(() =>
            {
                Debug.LogWarning($"[Ads] Rewarded show failed: {error.GetMessage()}");

                var callback = _onRewardedResult;
                _onRewardedResult = null;
                _rewardEarned = false;

                // 실패 경로도 게임 콜백을 부른다(부활 버튼 되살리기 등).
                // 재로드를 finally 로 보장하는 이유는 위 닫힘 경로와 동일하다.
                try
                {
                    callback?.Invoke(false);
                }
                finally
                {
                    LoadRewarded();
                }
            });
        }

        public void ShowRewarded(Action<bool> onResult)
        {
            _onRewardedResult = onResult;
            _rewardEarned = false;

            // 철회 상태에서는 노출하지 않는다. 보상형은 결과가 false여야 부활이 지급되지 않는다.
            if (!ConsentManager.CanRequestAds)
            {
                DiscardLoadedAds();

                var denied = _onRewardedResult;
                _onRewardedResult = null;
                denied?.Invoke(false);
                return;
            }

            if (_rewardedAd != null && _rewardedAd.CanShowAd())
            {
                _rewardedAd.Show((Reward reward) => AdMainThread.Run(() =>
                {
                    // 이 콜백은 유저가 광고 끝까지 봤을 때 호출됨.
                    // Run은 예약 순서를 지키므로, 아래 닫힘 콜백보다 항상 먼저 처리된다.
                    _rewardEarned = true;
                    Debug.Log($"[Ads] Reward earned: {reward.Type} x{reward.Amount}");
                }));
            }
            else
            {
#if UNITY_EDITOR
                Debug.LogWarning("Rewarded not ready. Returning false.");
#endif
                var callback = _onRewardedResult;
                _onRewardedResult = null;

                // 게임 콜백이 던져도 다음 기회를 위한 로드는 반드시 시도한다.
                try
                {
                    callback?.Invoke(false);
                }
                finally
                {
                    LoadRewarded();
                }
            }
        }

        // ════════════════════════════════════════════
        // 동의 철회 대응
        // ════════════════════════════════════════════

        /// <summary>
        /// 로드해 둔 광고를 전부 파기한다.
        ///
        /// AdMob SDK는 요청 시점의 동의 상태로 광고를 받아 두므로, 철회 이후에도
        /// 이미 로드된 인스턴스는 그대로 노출 가능한 상태로 남는다. 명시적으로 버려야 한다.
        /// 재로드는 하지 않는다 — Load* 가 동의를 다시 확인하므로 어차피 요청되지 않고,
        /// 여기서 호출하면 파기와 재로드가 서로를 부르는 모양이 된다.
        /// </summary>
        public void DiscardLoadedAds()
        {
            if (_interstitialAd != null)
            {
                _interstitialAd.Destroy();
                _interstitialAd = null;
            }

            if (_rewardedAd != null)
            {
                _rewardedAd.Destroy();
                _rewardedAd = null;
            }
        }
    }
}