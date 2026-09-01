#if !UNITY_WEBGL
using System;
using GoogleMobileAds.Ump.Api;
using UnityEngine;

namespace IGMain.Ads
{
    /// <summary>
    /// UMP(User Messaging Platform) 동의 수집.
    ///
    /// EEA/영국 사용자에게 개인정보 동의 팝업을 띄우지 않고 광고를 요청하면
    /// AdMob 정책 위반이다. 이 클래스가 동의 상태를 갱신하고 필요 시 폼을 띄운다.
    ///
    /// 동의가 불필요한 지역(예: 한국)에서는 팝업 없이 즉시 완료 콜백이 온다.
    ///
    /// 주의: 실제로 어떤 문구의 폼이 뜨는지는 코드가 아니라
    /// AdMob 콘솔의 '개인정보 및 메시지' 설정에서 정한다.
    /// 콘솔에서 메시지를 만들어 게시하지 않으면 EEA에서도 폼이 뜨지 않는다.
    /// </summary>
    public static class ConsentManager
    {
        /// <summary>
        /// 광고 요청이 허용된 상태인지. Update() 호출 전에는 항상 false.
        /// </summary>
        public static bool CanRequestAds => ConsentInformation.CanRequestAds();

        /// <summary>이 설치에서 동의가 한 번이라도 확보된 적이 있는지 기록하는 키.</summary>
        private const string PrefEverGranted = "Consent.EverGranted";

        /// <summary>
        /// 사용자가 **거부하거나 철회했다**고 판단되는 상태.
        ///
        /// CanRequestAds가 false인 것만으로는 "아직 응답 안 함"과 "거부함"을 구분할 수 없다.
        /// 그래서 상태값이 아니라 **전이**를 본다 — 한 번 확보됐던 동의가 지금은 없다면 철회다.
        ///
        /// ConsentStatus로 판정하지 않는 이유:
        /// - `!= Unknown`은 Required(= 동의가 필요한데 아직 못 받음)를 거부로 오판한다.
        ///   EEA 사용자가 네트워크 문제로 폼을 못 받거나, AdMob 콘솔에 메시지를 게시하지 않아
        ///   폼 자체가 뜨지 않는 경우에도 그 분기를 타서, 거부한 적 없는 사용자의
        ///   Crashlytics 수집이 꺼진다.
        /// - `== Obtained`는 반대 위험이 있다. 철회 후 상태가 Required로 되돌아가면
        ///   철회를 영영 감지하지 못해 이 기능의 존재 이유가 사라진다.
        ///
        /// 두 경우 모두에 안전한 것은 "예전엔 됐는데 지금은 안 된다"는 사실뿐이다.
        /// </summary>
        public static bool IsConsentDenied =>
            PlayerPrefs.GetInt(PrefEverGranted, 0) == 1 &&
            !ConsentInformation.CanRequestAds();

        /// <summary>
        /// 동의 상태가 확정될 때마다 발생한다 (Gather 완료 / 개인정보 옵션 폼 종료).
        ///
        /// UMP 수집은 비동기라 UI가 처음 그려지는 시점에는 IsPrivacyOptionsRequired가
        /// 아직 Unknown이다. 이 이벤트로 확정 시점에 진입점 노출 여부를 다시 판정한다.
        /// 항상 메인 스레드에서 발생한다.
        /// </summary>
        public static event Action OnConsentResolved;

        /// <summary>
        /// 동의 상태를 갱신하고 필요 시 폼을 노출한다.
        /// 성공/실패와 무관하게 onComplete가 정확히 한 번 호출된다.
        /// (실패해도 흐름이 막히면 안 되므로. 실제 광고 가능 여부는 CanRequestAds로 판단할 것)
        /// </summary>
        public static void Gather(Action onComplete)
        {
#if DEBUG_CONSENT
            // 검증 편의: 매 실행마다 동의 상태를 초기화해 폼이 항상 다시 뜨게 한다.
            ConsentInformation.Reset();
#endif
            var parameters = new ConsentRequestParameters
            {
                // 아동 대상 앱이 아님. 아동 타겟으로 전환 시 true 및 별도 정책 검토 필요.
                TagForUnderAgeOfConsent = false,
#if DEBUG_CONSENT
                // EEA 사용자를 시뮬레이션해 폼 노출을 테스트한다.
                // TestDeviceHashedIds에는 기기 로그(logcat/Xcode)에 찍히는
                // "Use ConsentDebugSettings.TestDeviceHashedIds" 메시지의 해시값을 넣을 것.
                // 해시값이 비어 있으면 디버그 지역 설정이 적용되지 않는다.
                ConsentDebugSettings = new ConsentDebugSettings
                {
                    DebugGeography = DebugGeography.EEA,
                    TestDeviceHashedIds = new System.Collections.Generic.List<string>
                    {
                        "1095276304D5E5961B702E806CCF06EC", // SM-S938N (개발기기)
                    },
                },
#endif
            };

            // 디스패처는 메인 스레드에서 미리 깨워둬야 한다. (Gather는 메인 스레드에서 호출됨)
            AdMainThread.EnsureInitialized();

            // UMP 콜백은 Java 백그라운드 스레드에서 올라온다.
            // 그 스레드에서 Unity API나 GMA를 만지면
            // "get_isPlaying can only be called from the main thread" 예외가 난다.
            // 그래서 콜백 본문을 전부 메인 스레드로 넘긴다.
            ConsentInformation.Update(parameters, updateError =>
            {
                RunOnMainThread(() =>
                {
                    if (updateError != null)
                    {
                        Debug.LogWarning($"[Consent] Update failed: {updateError.Message}");
                        NotifyResolved();
                        onComplete?.Invoke();
                        return;
                    }

                    // 폼이 필요 없는 지역이면 아무것도 띄우지 않고 즉시 콜백.
                    ConsentForm.LoadAndShowConsentFormIfRequired(formError =>
                    {
                        RunOnMainThread(() =>
                        {
                            if (formError != null)
                            {
                                Debug.LogWarning($"[Consent] Form failed: {formError.Message}");
                            }

                            NotifyResolved();
                            onComplete?.Invoke();
                        });
                    });
                });
            });
        }

        private static void NotifyResolved()
        {
            // 동의가 확보된 적이 있다는 사실을 남긴다. IsConsentDenied가
            // "아직 응답 안 함"과 "철회함"을 구분하는 유일한 근거이므로,
            // **구독자에게 알리기 전에** 기록해야 한다.
            // 한 번 세우면 지우지 않는다 — 철회했다가 다시 동의해도 CanRequestAds가
            // true로 돌아오므로 판정은 저절로 맞는다.
            if (ConsentInformation.CanRequestAds() && PlayerPrefs.GetInt(PrefEverGranted, 0) == 0)
            {
                PlayerPrefs.SetInt(PrefEverGranted, 1);
                PlayerPrefs.Save();
            }

            // 구독자 하나가 던져도 **나머지 구독자가** 실행돼야 한다.
            // 멀티캐스트 델리게이트를 그냥 Invoke()하고 전체를 try로 감싸면
            // 예외 이후의 구독자는 아예 호출되지 않는다 — 동의 흐름만 지켜지고
            // 구독자 간 격리는 안 된다. 예를 들어 AdManager.OnConsentChanged가 던지면
            // MainPanel.RefreshPrivacyOptionsButton이 누락돼 동의 철회 진입점
            // (Privacy Options 버튼)이 갱신되지 않는다. 그래서 구독자별로 끊어 호출한다.
            var handlers = OnConsentResolved;
            if (handlers == null) return;

            foreach (var handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action)handler).Invoke();
                }
                catch (Exception e)
                {
                    // 어느 구독자가 범인인지 알아야 하므로 메서드 이름까지 남긴다.
                    Debug.LogWarning(
                        $"[Consent] OnConsentResolved 구독자 예외 ({handler.Method.Name}): {e.Message}");
                }
            }
        }

        /// <summary>
        /// 개인정보 옵션 폼을 노출해야 하는 사용자인지.
        ///
        /// AdMob 콘솔에서 '개인정보 옵션' 메시지를 게시했고 사용자가 EEA/영국 등
        /// 해당 지역에 있을 때만 true다. 그 외 지역에서는 버튼을 숨겨야 한다
        /// (불필요한 UI 노출 방지). Gather() 완료 전에는 항상 false.
        /// </summary>
        public static bool IsPrivacyOptionsRequired =>
            ConsentInformation.PrivacyOptionsRequirementStatus ==
            PrivacyOptionsRequirementStatus.Required;

        /// <summary>
        /// 개인정보 옵션 폼을 띄운다. 사용자가 이미 한 동의를 변경·철회하는 경로다.
        ///
        /// GDPR 제7조는 동의 철회가 동의만큼 쉬울 것을 요구하고, UMP도 이 진입점을
        /// 앱 안에 두도록 요구한다. 설정 화면의 버튼에서 호출할 것.
        /// 성공/실패와 무관하게 onComplete가 정확히 한 번 호출된다.
        /// </summary>
        public static void ShowPrivacyOptions(Action onComplete = null)
        {
            // UMP 콜백은 Gather()와 마찬가지로 Java 백그라운드 스레드에서 올라온다.
            AdMainThread.EnsureInitialized();

            ConsentForm.ShowPrivacyOptionsForm(formError =>
            {
                RunOnMainThread(() =>
                {
                    if (formError != null)
                        Debug.LogWarning($"[Consent] Privacy options form failed: {formError.Message}");

                    NotifyResolved();
                    onComplete?.Invoke();
                });
            });
        }

        private static void RunOnMainThread(Action action)
        {
            AdMainThread.Run(action);
        }

#if UNITY_EDITOR || DEBUG_ADS
        /// <summary>[디버그] 동의 상태를 초기화한다. 폼을 다시 띄워 테스트할 때 사용.</summary>
        public static void DebugReset() => ConsentInformation.Reset();

        /// <summary>[디버그] 현재 동의 상태 문자열.</summary>
        public static string DebugStatus =>
            $"status={ConsentInformation.ConsentStatus}, canRequestAds={ConsentInformation.CanRequestAds()}";
#endif
    }
}
#else
using System;

namespace IGMain.Ads
{
    /// <summary>
    /// WebGL(앱인토스) 스텁.
    ///
    /// UMP는 WebGL 구현체가 없어 <c>ConsentInformation.Update</c>가
    /// <c>Utils.GetClientFactory()</c>에서 null 타입으로 <c>ArgumentNullException</c>을 던진다.
    /// 그 호출이 <c>TitleScene.Awake</c> 스택 안이라 씬 초기화가 통째로 중단되고
    /// 화면이 백지가 된다 (2026-07-31 브라우저에서 재현·확인).
    ///
    /// 앱인토스에서는 동의 수집을 토스가 담당하므로 이 클래스가 할 일이 없다.
    /// 호출부(AdManager·MainPanel)를 무수정으로 두기 위해 표면만 유지한다.
    /// 광고 자체는 P1에서 AitAdProvider가 맡는다 (AIT_PLAN.md 참조).
    /// </summary>
    public static class ConsentManager
    {
        /// <summary>앱인토스 경로에서는 이 클래스가 광고 권한을 판정하지 않는다.</summary>
        public static bool CanRequestAds => false;

        /// <summary>철회 개념이 없다. Firebase 스텁도 no-op이라 소비처가 없다.</summary>
        public static bool IsConsentDenied => false;

        /// <summary>동의 옵션 진입점(설정 버튼)을 숨긴다.</summary>
        public static bool IsPrivacyOptionsRequired => false;

        public static event Action OnConsentResolved;

        /// <summary>
        /// 즉시 "확정됨"으로 처리한다. 원본과 동일하게 onComplete를 정확히 한 번 호출하고,
        /// 구독자에게도 알려 MainPanel이 개인정보 옵션 버튼을 숨기도록 한다.
        /// </summary>
        public static void Gather(Action onComplete)
        {
            NotifyResolved();
            onComplete?.Invoke();
        }

        public static void ShowPrivacyOptions(Action onComplete = null)
        {
            NotifyResolved();
            onComplete?.Invoke();
        }

        /// <summary>원본과 같은 구독자별 예외 격리를 유지한다.</summary>
        private static void NotifyResolved()
        {
            var handlers = OnConsentResolved;
            if (handlers == null) return;

            foreach (var handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action)handler).Invoke();
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning(
                        $"[Consent] OnConsentResolved 구독자 예외 ({handler.Method.Name}): {e.Message}");
                }
            }
        }

#if UNITY_EDITOR || DEBUG_ADS
        public static void DebugReset() { }
        public static string DebugStatus => "webgl-stub (UMP 미사용)";
#endif
    }
}
#endif
