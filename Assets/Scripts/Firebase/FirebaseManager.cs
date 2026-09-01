#if !UNITY_WEBGL
using System.Collections.Generic;
using Firebase.Extensions;   // ContinueWithOnMainThread (Firebase.TaskExtension.dll)
using UnityEngine;

namespace IGMain
{
    /// <summary>
    /// Firebase 초기화 및 Crashlytics 설정.
    /// IGEngine.Awake() 에서 가장 먼저 호출한다.
    ///
    /// Analytics 수집은 매니페스트에서 기본 꺼짐이며(firebase_analytics_collection_enabled=false),
    /// UMP 동의를 받은 뒤 ApplyConsent()가 런타임에 켠다.
    /// 매니페스트 설정 없이 이 코드만 두면 FirebaseInitProvider가 프로세스 시작 시점에
    /// 이미 수집을 시작해버리므로, 둘은 반드시 함께 유지해야 한다.
    /// </summary>
    public static class FirebaseManager
    {
        /// <summary>Firebase 의존성 확인이 끝나 Analytics API를 호출해도 되는 상태인지.</summary>
        public static bool IsReady { get; private set; }

        // 의존성 확인이 끝나기 전에 동의 결과가 먼저 도착할 수 있다.
        // 그 경우 값을 보관해 두었다가 준비된 뒤 적용한다.
        private static bool? _pendingConsent;
        private static bool  _pendingDenied;

        // TitleScene과 IGEngine 양쪽에서 호출되므로 중복 초기화를 막는다.
        private static bool _initializeStarted;

        /// <summary>
        /// Firebase를 초기화한다. **첫 씬(TitleScene)에서 가장 먼저 호출해야 한다.**
        ///
        /// 이 호출이 늦으면 IsReady가 false인 동안 ApplyConsent가 버퍼링만 되고
        /// Firebase SDK에 도달하지 않는다. 동의 수집·철회는 타이틀 화면에서 일어나므로,
        /// IGScene 진입 전까지 철회가 반영되지 않는 구멍이 생긴다.
        /// (SetAnalyticsCollectionEnabled는 영구 저장되므로 앱을 재시작해도 켜진 채 남는다)
        ///
        /// 여러 번 불러도 안전하다.
        /// </summary>
        public static void Initialize()
        {
            if (_initializeStarted) return;
            _initializeStarted = true;

            // ContinueWith 가 아니라 **ContinueWithOnMainThread** 를 쓴다.
            //
            // 스케줄러를 지정하지 않은 ContinueWith 는 continuation 을 ThreadPool 스레드에서
            // 실행한다. 그러면 아래 pending 드레인이 ApplyConsent → FirebaseAnalytics.SetConsent 를
            // 비메인 스레드에서 호출하게 된다(동의가 의존성 확인보다 먼저 도착하는 콜드 스타트).
            // 더 나쁜 것은 ApplyConsent 의 재진입 가드(_applying/_hasQueued — 비휘발 static bool)가
            // 단일 스레드를 전제로 만들어졌다는 점이다. 메인 스레드 호출과 풀 스레드 드레인이 겹치면
            // 양쪽이 모두 가드를 통과해 SetConsent 가 동시에 돌고, 가드가 막으려던 바로 그 네이티브
            // 교착이 재발한다. 메인 스레드로 고정하면 단일 스레드 전제가 성립해 가드가 설계대로 동작한다.
            Firebase.FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
            {
                if (task.Result == Firebase.DependencyStatus.Available)
                {
                    // C# 예외를 fatal로 분류 — 없으면 Crashlytics 대시보드에서 안 보일 수 있음
                    Firebase.Crashlytics.Crashlytics.ReportUncaughtExceptionsAsFatal = true;
                    IsReady = true;
                    IGLog.Verbose("[FirebaseManager] Crashlytics initialized.");

                    if (_pendingConsent.HasValue)
                    {
                        bool granted = _pendingConsent.Value;
                        bool denied  = _pendingDenied;
                        _pendingConsent = null;
                        _pendingDenied  = false;
                        ApplyConsent(granted, denied);
                    }
                }
                else
                {
                    Debug.LogError($"[FirebaseManager] Dependency check failed: {task.Result}");
                }
            });
        }

        /// <summary>
        /// UMP 동의 결과를 Firebase에 반영한다.
        /// AdManager가 동의 수집 완료 시, MainPanel이 동의 철회 폼을 닫을 때 호출한다.
        ///
        /// granted=true  : 저장소 동의 확보 → 수집 활성화
        /// granted=false : 동의 거부·수집 실패 → 수집 비활성 유지 (매니페스트 기본값 그대로)
        ///
        /// **AdStorage 하나만 설정한다.**
        /// 인자로 받는 값은 UMP의 CanRequestAds() 하나뿐인데, 이 값은 TCF 목적1(기기 정보 저장)만
        /// 승낙하고 목적3·4(개인화 프로필/광고)를 거부해도 true다. 그 하나의 불리언을
        /// AdUserData/AdPersonalization까지 Granted로 확대 적용하면 사용자가 명시적으로 거부한
        /// 항목을 동의한 것으로 구글에 보고하게 된다.
        /// 비워두면 Android Analytics SDK가 CMP가 기록한 IABTCF_* 값에서 스스로 판정하므로,
        /// 잘못된 신호를 보내는 것보다 정확하다.
        ///
        /// AnalyticsStorage도 같은 이유로 2026-07-29에 제외했다. 목적1은 저장·접근 일반에 대한
        /// 것이고 측정은 목적7~9라, 목적1만 승낙한 사용자에게 AnalyticsStorage=Granted를 보내면
        /// 사용자가 고른 것보다 넓게 동의를 보고하게 된다. AdStorage는 목적1의 의미와 일치하므로
        /// 남겨 둔다. (수집 자체의 on/off는 아래 SetAnalyticsCollectionEnabled가 여전히 담당한다 —
        /// 이건 구글에 보내는 동의 신호가 아니라 앱 로컬 스위치다)
        /// </summary>
        /// <param name="granted">저장소 동의 확보 여부 (ConsentManager.CanRequestAds)</param>
        /// <param name="denied">
        /// 동의 상태가 **확정된 뒤** 거부·철회로 판정된 경우 true (ConsentManager.IsConsentDenied).
        /// 아직 수집 전(Unknown)과 구분하기 위한 값이며 Crashlytics 중단 판단에만 쓴다.
        /// </param>
        public static void ApplyConsent(bool granted, bool denied = false)
        {
            if (!IsReady)
            {
                // 의존성 확인이 아직 안 끝났다. 준비되면 적용한다.
                _pendingConsent = granted;
                _pendingDenied = denied;
                return;
            }

            // ── 재진입 방어 ──────────────────────────────────────────────────
            //
            // FirebaseAnalytics.SetConsent 가 도는 동안 Unity 메인 루프는 계속 펌프된다.
            // 그 틈에 UMP 콜백이 배달되어 이 메서드가 **같은 스레드에서 다시** 호출될 수 있고,
            // 그대로 중첩시키면 네이티브 락에서 교착이 나 메인 스레드가 영구히 멈춘다.
            // (2026-07-30 실기기 재현: SetConsent 진입 → 반환 전에 두 번째 SetConsent 진입 → 정지)
            //
            // 중첩 호출은 버리지 않고 예약해 뒀다가 바깥 호출이 끝난 뒤 한 번 더 적용한다 —
            // 값이 다를 수 있으므로 마지막 상태가 반드시 반영되어야 한다.
            if (_applying)
            {
                _queuedGranted = granted;
                _queuedDenied  = denied;
                _hasQueued     = true;
                return;
            }

            _applying = true;
            try
            {
                ApplyConsentCore(granted, denied);
            }
            finally
            {
                _applying = false;
            }

            if (_hasQueued)
            {
                _hasQueued = false;
                ApplyConsent(_queuedGranted, _queuedDenied);
            }
        }

        private static bool _applying;
        private static bool _hasQueued;
        private static bool _queuedGranted;
        private static bool _queuedDenied;

        private static void ApplyConsentCore(bool granted, bool denied)
        {
            try
            {
                var status = granted
                    ? Firebase.Analytics.ConsentStatus.Granted
                    : Firebase.Analytics.ConsentStatus.Denied;

                var consent = new Dictionary<Firebase.Analytics.ConsentType,
                                             Firebase.Analytics.ConsentStatus>
                {
                    { Firebase.Analytics.ConsentType.AdStorage, status },
                    // AdUserData / AdPersonalization 은 의도적으로 설정하지 않는다 (위 주석 참조).
                };

                // AnalyticsStorage는 **거부가 확정된 경우에만** 명시적으로 Denied를 보낸다.
                //
                // 미지정으로 두면 SDK가 IABTCF_* 에서 알아서 판정할 것 같지만 그렇지 않다 —
                // TCF 목적 체계에서 유도되는 것은 광고 계열(ad_storage / ad_user_data /
                // ad_personalization)뿐이고 analytics_storage는 그 체계에 없어서
                // **미지정이면 기본값 granted로 취급된다.**
                // 따라서 그냥 빼면 거부 사용자에 대한 거부 신호가 사라진다.
                //
                // 반대로 granted 쪽은 명시하지 않는다. 인자로 받는 값이 CanRequestAds 하나뿐인데
                // 그건 TCF 목적1만 승낙해도 true라, 측정 목적(7~9)까지 동의한 것으로
                // 보고하게 되기 때문이다.
                if (denied)
                {
                    consent[Firebase.Analytics.ConsentType.AnalyticsStorage] =
                        Firebase.Analytics.ConsentStatus.Denied;
                }

                Firebase.Analytics.FirebaseAnalytics.SetConsent(consent);
                Firebase.Analytics.FirebaseAnalytics.SetAnalyticsCollectionEnabled(granted);

                ApplyCrashlyticsConsent(granted, denied);

                IGLog.Verbose($"[FirebaseManager] Consent applied: granted={granted}, denied={denied}");
            }
            catch (System.Exception e)
            {
                // 분석 설정 실패가 게임 진행을 막아서는 안 된다.
                Debug.LogWarning($"[FirebaseManager] 동의 적용 실패: {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>
        /// Crashlytics 수집을 동의 상태에 맞춘다.
        ///
        /// **기본값(활성)을 그대로 둔다.** 매니페스트에 firebase_crashlytics_collection_enabled=false를
        /// 넣어 동의 확정 전까지 끄는 방법도 있지만, 그러면 앱 시작 직후 구간의 크래시가 통째로
        /// 사라진다. 가장 진단이 필요한 구간이 정확히 거기다. 크래시 진단은 통상 '정당한 이익'으로
        /// 다루어지고 개인정보처리방침에도 고지돼 있으므로 그 상태를 유지한다.
        ///
        /// 다만 사용자가 **명시적으로 거부·철회**한 경우에는 그 의사를 따라 수집을 중단한다.
        /// GDPR 제7조 3항이 요구하는 것은 철회가 동의만큼 쉬울 것이지, 확정 전 구간까지
        /// 수집하지 말라는 것이 아니다. denied와 granted를 따로 받는 이유가 이것이다 —
        /// CanRequestAds가 false인 것만으로는 '아직 모름'과 '거부'가 구분되지 않는다.
        /// </summary>
        private static void ApplyCrashlyticsConsent(bool granted, bool denied)
        {
            if (denied)
            {
                Firebase.Crashlytics.Crashlytics.IsCrashlyticsCollectionEnabled = false;
                IGLog.Verbose("[FirebaseManager] 동의 철회 — Crashlytics 수집 중단");
            }
            else if (granted)
            {
                // 이전 세션에서 철회했다가 다시 동의한 경우를 되살린다.
                // 이 설정은 영구 저장되므로 명시적으로 켜 주지 않으면 꺼진 채로 남는다.
                Firebase.Crashlytics.Crashlytics.IsCrashlyticsCollectionEnabled = true;
            }
            // granted도 denied도 아닌 상태(= 아직 확정 전)에서는 아무것도 하지 않는다.
        }
    }
}
#else
namespace IGMain
{
    /// <summary>
    /// WebGL(앱인토스) 스텁. Firebase Unity SDK는 WebGL을 지원하지 않으므로
    /// 파사드 표면만 유지해 호출부(IGEngine·TitleScene·AdManager·MainPanel)를 무수정으로 둔다.
    /// 분석·크래시 리포팅은 앱인토스 경로에서 AIT Analytics/Sentry가 대신한다 (AIT_PLAN.md P1-5).
    /// </summary>
    public static class FirebaseManager
    {
        public static bool IsReady => false;

        public static void Initialize() { }

        public static void ApplyConsent(bool granted, bool denied = false) { }
    }
}
#endif
