using System;
using UnityEngine;

namespace IGMain.Ads
{
    /// <summary>
    /// 광고 시스템의 단일 진입점.
    /// 게임 코드는 AdManager.Instance.TryShowInterstitial() / ShowRewarded() 만 호출.
    ///
    /// 저수준 mechanism(ShowInterstitial/ShowRewarded)과
    /// 고수준 policy(TryShowInterstitial)가 여기서 연결된다.
    /// SDK 교체 시 InitializeManager() 안에서 _provider 한 줄만 변경.
    /// </summary>
    public class AdManager : ManagerBase<AdManager>
    {
        private IAdProvider  _provider;
        private AdGatePolicy _gatePolicy;

        // TitleScene은 게임에서 돌아올 때마다 다시 로드되므로 InitializeManager도 반복 호출된다.
        // SDK 중복 초기화와 Provider 교체(= 로드된 광고 유실)를 막는다.
        private bool _initialized;

        public override void InitializeManager()
        {
            if (_initialized) return;
            _initialized = true;

            _gatePolicy = new AdGatePolicy();

#if UNITY_WEBGL
            // 앱인토스 경로: 동의 수집(UMP)도 Firebase 반영도 없다.
            // 개인정보 동의와 광고 정책은 토스 앱이 쥐고 있고, WebGL의 ConsentManager는
            // 스텁이라 CanRequestAds=false 를 돌려준다. 아래 원본 흐름을 그대로 태우면
            // "동의 없음"으로 판정되어 provider가 영영 만들어지지 않는다.
            //
            // 광고 게이트 정책(AdGatePolicy)은 플랫폼과 무관하므로 위에서 그대로 만든다.
            InitializeProvider();
#else
            // 동의 상태가 바뀔 때마다(수집 완료 / 개인정보 옵션 폼 종료) 반영한다.
            // 같은 메서드 그룹이라 -= 로 중복 구독이 제거되므로, 동의 거부로 _initialized가
            // 풀려 이 메서드가 다시 실행돼도 구독은 하나만 남는다.
            ConsentManager.OnConsentResolved -= OnConsentChanged;
            ConsentManager.OnConsentResolved += OnConsentChanged;

            // 동의를 먼저 수집한 뒤 SDK를 초기화한다.
            // (ConsentManager가 콜백을 메인 스레드로 넘겨주므로 아래 람다는 메인 스레드에서 실행된다)
            // 순서가 뒤집히면 EEA 사용자에게 동의 없이 광고를 요청하게 되어 정책 위반.
            ConsentManager.Gather(() =>
            {
                bool granted = ConsentManager.CanRequestAds;

                // 동의 결과를 Firebase에도 반영한다. Analytics는 매니페스트에서 기본 꺼짐이므로,
                // 이 호출이 있어야만 수집이 켜진다. 거부 시에는 꺼진 채로 유지된다.
                FirebaseManager.ApplyConsent(granted, ConsentManager.IsConsentDenied);

                if (!granted)
                {
                    // 동의 거부 또는 수집 실패. 광고 없이 게임은 정상 진행된다.
                    // 가드를 풀어 다음 타이틀 진입 때 다시 시도하게 한다.
                    // (일시적 네트워크 오류로 앱 재시작 전까지 광고가 죽는 것을 방지)
                    Debug.LogWarning("[AdManager] Consent not granted. Ads disabled, will retry.");
                    _initialized = false;
                    return;
                }

                InitializeProvider();
            });

            // 구글 표준 흐름: 위 비동기 수집과 별개로, 지금 당장 광고가 가능한지도 확인한다.
            // 이전 세션에서 받은 동의가 캐시돼 있으면 응답을 기다리지 않고 즉시 초기화되고,
            // UMP 서버 조회가 실패해도 캐시된 동의로 정상 동작한다.
            if (ConsentManager.CanRequestAds)
            {
                FirebaseManager.ApplyConsent(true);
                InitializeProvider();
            }
#endif
        }

        /// <summary>
        /// 동의 상태가 확정될 때마다 호출된다 (Gather 완료 / 개인정보 옵션 폼 종료).
        ///
        /// 철회 경로가 이 메서드의 존재 이유다. 이것이 없으면 사용자가 개인정보 옵션에서
        /// 동의를 전부 거둬도 이미 초기화된 provider가 그 세션 동안 광고를 계속 요청·노출한다
        /// (다음 앱 재시작 전까지 철회 의사가 광고에 반영되지 않는다).
        /// </summary>
        private void OnConsentChanged()
        {
            bool granted = ConsentManager.CanRequestAds;

            // ⚠ **여기서 FirebaseManager.ApplyConsent 를 부르지 말 것.**
            //
            // 이 메서드는 ConsentManager.NotifyResolved() 안에서, 즉 UMP 콜백 스택 위에서 실행된다.
            // 그런데 AdManager.InitializeManager 의 "캐시된 동의" 경로가 이미 ApplyConsent 를
            // 호출해 FirebaseAnalytics.SetConsent 안에 들어가 있을 수 있다. SetConsent 가 도는 동안
            // Unity 메인 루프는 계속 펌프되므로 그 틈에 UMP 콜백이 배달되어 이 메서드가 실행되고,
            // 여기서 다시 ApplyConsent 를 부르면 SetConsent 가 **재진입**한다.
            // 그러면 네이티브 락에서 교착이 나 메인 스레드가 영구히 멈추고, 화면은 마지막
            // 프레임인 채로 터치가 전혀 먹지 않는다 (2026-07-30 실기기에서 재현·확인).
            //
            // Firebase 반영은 이미 두 곳이 담당한다. 둘 다 NotifyResolved() **바깥**이라 안전하다:
            //   - AdManager.InitializeManager 의 ConsentManager.Gather onComplete
            //   - MainPanel.OpenPrivacyOptions 의 ShowPrivacyOptions onComplete (철회 경로)
            // 따라서 여기서 빼도 기능 손실이 없다.

            if (!granted)
            {
                // 이미 로드된 광고를 버린다. Load*/Show* 에도 가드가 있지만,
                // 여기서 버려야 "철회했는데 아직 광고가 준비돼 있다"는 상태가 남지 않는다.
                _provider?.DiscardLoadedAds();
                return;
            }

            // 거부했다가 다시 동의한 경우. 최초 시도 때 provider가 만들어지지 않았다면 지금 만든다.
            if (_provider == null)
            {
                InitializeProvider();
                return;
            }

            // provider는 살아 있는데 가드 때문에 로드가 막혀 있던 상태를 되살린다.
            if (!_provider.IsInterstitialReady) _provider.LoadInterstitial();
            if (!_provider.IsRewardedReady)     _provider.LoadRewarded();
        }

        /// <summary>
        /// AdMob SDK를 초기화한다. 동의 수집 콜백과 캐시 확인 양쪽에서 호출되므로
        /// 중복 초기화(= 로드된 광고 유실)를 막기 위해 멱등해야 한다.
        /// </summary>
        private void InitializeProvider()
        {
            if (_provider != null) return;

            // SDK 교체 시 이 한 줄만 바꾸세요.
#if UNITY_WEBGL
            _provider = new AitAdProvider();
#else
            _provider = new AdMobProvider();
#endif
            _provider.Initialize();

#if UNITY_EDITOR
            Debug.Log($"<color=cyan>AdManager initialized with {_provider.GetType().Name}</color>");
#endif
        }

        public override void ClearManager()  { }

        public override void FinalizeManager()
        {
            // ConsentManager는 static이라 이벤트가 이 인스턴스를 붙잡고 있다.
            ConsentManager.OnConsentResolved -= OnConsentChanged;
        }

        // ── 상태 조회 ──────────────────────────────────────────────────────────

        public bool IsInterstitialReady => _provider?.IsInterstitialReady ?? false;
        public bool IsRewardedReady     => _provider?.IsRewardedReady     ?? false;

        // ── 고수준 API (게임 코드가 사용하는 진입점) ────────────────────────────

        /// <summary>
        /// 게임오버 1회를 정책에 통보한다. **노출 시도와 분리되어 있다.**
        ///
        /// 예전에는 TryShowInterstitial 첫 줄에서 함께 처리했는데, 실패 연출을 넣으면서
        /// 그 호출이 1.2초 지연 코루틴 안으로 들어가 **게임오버 직후 앱이 죽으면 카운터가
        /// 아예 증가하지 않는** 구멍이 생겼다. 치팅(1.2초 안에 force-stop 반복)뿐 아니라
        /// 게임오버 직후 앱을 닫는 습관만으로도 전면 광고가 영구히 사라진다.
        ///
        /// 그래서 집계는 게임오버가 **확정되는 순간 동기로** 끝낸다
        /// (IGGameController.CheckGameOver). 노출 판정·노출만 지연시킨다.
        /// AdGatePolicy 가 카운터를 즉시 디스크에 쓰므로 이 호출로 영속까지 완료된다.
        /// </summary>
        public void NotifyGameOver()
        {
            _gatePolicy?.NotifyGameOver();
        }

        /// <summary>
        /// 정책이 허가하면 전면 광고를 노출한다.
        /// 정책 미통과 또는 광고 미준비 시에도 onClosed를 즉시 호출하여 게임 흐름을 보장한다.
        ///
        /// **게임오버 집계는 하지 않는다** — NotifyGameOver 주석 참고.
        /// </summary>
        public void TryShowInterstitial(Action onClosed = null)
        {
            if (!_gatePolicy.ShouldShowInterstitial())
            {
                onClosed?.Invoke();
                return;
            }

            // 실제로 노출된 경우에만 게이트를 리셋한다.
            //
            // provider가 없거나, 동의가 없거나, 광고가 준비되지 않았거나, Show가 실패하면
            // ShowInterstitial은 아무것도 띄우지 못하고 콜백을 즉시 돌려준다. 거기서도
            // "노출했다"고 기록하면 게임오버 카운터가 0이 되고 90초 쿨다운이 시작된다.
            // 예전에는 이 오염이 세션 메모리라 재시작하면 사라졌지만, 카운터를 영속화한
            // 지금은 디스크에 남는다. fill이 나쁜 구간이나 오프라인 플레이가 잦은 사용자는
            // 3번째 게임오버마다 게이트만 리셋되고 광고는 계속 안 나가게 된다 —
            // 영속화의 목적과 정반대다.
            //
            // 판정은 provider가 콜백으로 돌려주는 shown 값에만 의존한다. 예전에는 Show 호출
            // **이전**의 준비 상태로 willShow를 미리 계산했는데, 그러면 준비까지 됐다가
            // 노출에 실패한 경우(OnAdFullScreenContentFailed — 성공 닫힘과 같은 콜백을 쓴다)를
            // 구분할 수 없어 실패에도 기록이 남았다. shown=true는 광고가 실제로 노출된 뒤
            // 닫혔을 때만 온다.
            //
            // 노출은 됐는데 닫기 전에 앱이 죽으면 기록이 남지 않는다. 이는 의도한 방향이다 —
            // 광고를 못 띄우고 게임오버를 소비하는 쪽이 훨씬 나쁘다.
            ShowInterstitial(shown =>
            {
                if (shown)
                    _gatePolicy.NotifyInterstitialShown();

                onClosed?.Invoke();
            });
        }

        // ── 저수준 API (Provider 위임) ─────────────────────────────────────────

        /// <summary>
        /// 전면 광고를 직접 노출한다. 정책 체크 없음.
        /// 게임 코드는 TryShowInterstitial 사용을 권장.
        ///
        /// onClosed(shown): shown=true 는 광고가 실제로 노출된 뒤 닫혔을 때만.
        /// 노출 실패·미동의·미준비·provider 없음은 모두 shown=false 로 즉시 돌아온다.
        /// </summary>
        public void ShowInterstitial(Action<bool> onClosed = null)
        {
            if (_provider == null) { onClosed?.Invoke(false); return; }
            _provider.ShowInterstitial(onClosed);
        }

        /// <summary>
        /// 리워드 광고를 노출한다. onResult(true) = 시청 완료, onResult(false) = 중도 이탈/실패.
        /// 시청 완료 시 policy에 자동 통보하여 다음 전면 광고 1회를 면제한다.
        /// </summary>
        public void ShowRewarded(Action<bool> onResult)
        {
            if (_provider == null) { onResult?.Invoke(false); return; }

            _provider.ShowRewarded(success =>
            {
                if (success)
                    _gatePolicy.NotifyRewardedShown();

                onResult?.Invoke(success);
            });
        }

        public void LoadInterstitial() => _provider?.LoadInterstitial();
        public void LoadRewarded()     => _provider?.LoadRewarded();

        // ── 디버그 전용 ────────────────────────────────────────────────────────

#if UNITY_EDITOR || DEBUG_ADS
        /// <summary>
        /// [디버그] 정책 체크 없이 전면 광고를 강제 노출한다.
        ///
        /// 노출 성공 여부를 구분할 필요가 없는 디버그 훅이라 인자 없는 Action 을 그대로 받는다.
        /// (ShowInterstitial 이 Action&lt;bool&gt; 로 바뀌었지만 여기서 흡수하므로 호출부는 그대로)
        /// </summary>
        public void DebugForceInterstitial(Action onClosed = null)
            => ShowInterstitial(_ => onClosed?.Invoke());

        /// <summary>[디버그] 정책 상태를 초기화한다.</summary>
        public void DebugResetPolicy() => _gatePolicy?.DebugReset();

        /// <summary>[디버그] 현재 정책 상태 문자열 반환.</summary>
        public string DebugPolicyStatus => _gatePolicy?.DebugStatus ?? "policy not initialized";

        /// <summary>[디버그] 현재 동의 상태 문자열 반환.</summary>
        public string DebugConsentStatus => ConsentManager.DebugStatus;

        /// <summary>[디버그] 동의 상태를 초기화한다. 앱 재시작 후 폼이 다시 뜬다.</summary>
        public void DebugResetConsent() => ConsentManager.DebugReset();
#endif
    }
}
