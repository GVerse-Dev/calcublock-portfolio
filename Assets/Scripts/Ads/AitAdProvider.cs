#if UNITY_WEBGL
using System;
using System.Collections.Generic;
using AppsInToss;
using UnityEngine;

namespace IGMain.Ads
{
    /// <summary>
    /// 앱인토스(Apps in Toss) 광고 구현.
    ///
    /// AdMob과 다른 점만 정리한다:
    /// - **동의 개념이 없다.** 개인정보 동의와 광고 정책은 토스 앱이 쥔다.
    ///   따라서 <see cref="ConsentManager"/>를 보지 않는다 (WebGL 스텁은 CanRequestAds=false를
    ///   돌려주므로 그 값을 태우면 광고가 영영 나가지 않는다).
    /// - **준비 여부를 SDK가 알려주지 않는다.** 전체 화면 광고에는 IsLoaded 조회가 없어
    ///   `loaded` 이벤트로 직접 상태를 들고 있어야 한다.
    /// - **노출하면 소비된다.** `dismissed` 이후에는 반드시 다시 Load 해야 한다(SDK 규약).
    /// - 콜백형 API가 구독 취소용 <see cref="Action"/>을 돌려주므로 끝날 때 반드시 호출한다.
    ///   안 그러면 AITCore의 구독 테이블에 죽은 항목이 쌓인다.
    ///
    /// 스레드: WebGL은 단일 스레드이고 SDK 이벤트도 SendMessage로 메인 스레드에 올라온다.
    /// AdMobProvider의 <c>AdMainThread</c> 마셜링에 해당하는 처리가 필요 없다.
    ///
    /// ⚠ 실제 광고는 **토스 앱(또는 샌드박스) 안에서만** 렌더된다. 일반 브라우저·에디터에서는
    /// 로드가 끝나지 않거나 에러 콜백이 오는 것이 정상이며, 그 경우 이 클래스는 항상
    /// "준비 안 됨"으로 동작해 게임 흐름을 그대로 통과시킨다.
    /// </summary>
    public class AitAdProvider : IAdProvider
    {
        /// <summary>전면/리워드가 상태 기계를 공유한다. 성공 판정 기준만 다르다.</summary>
        private sealed class Slot
        {
            public readonly string Label;

            /// <summary>진단 로그 이름에 넣을 짧은 식별자. 이름 길이를 아끼려고 따로 둔다.</summary>
            public readonly string ShortName;

            public readonly Func<string> AdGroupIdSource;

            /// <summary>보상 이벤트가 있어야 성공인가(리워드) — 노출만으로 성공인가(전면).</summary>
            public readonly bool RewardRequired;

            public bool IsReady;
            public bool IsLoading;

            /// <summary>로드가 필요하지만 로드 레인이 차 있어 순서를 기다리는 중인가.</summary>
            public bool WantsLoad;

            /// <summary>
            /// 로드 시도마다 증가한다. 구독을 해제한 뒤에도 SDK가 이미 큐에 넣은 콜백은
            /// 도착할 수 있어서, 지난 시도의 응답을 현재 상태에 반영하지 않으려면 필요하다.
            /// </summary>
            public int LoadGeneration;

            public Action UnsubscribeLoad;
            public Action UnsubscribeShow;

            public Action<bool> Callback;
            public bool Succeeded;
            public bool Finished;

            /// <summary>
            /// 이번 노출의 진단 로그를 이미 남겼는가. impression 과 show 가 함께 도착할 수
            /// 있어서, 없으면 노출 수가 부풀어 지표를 잘못 읽게 된다.
            /// </summary>
            public bool ImpressionTracked;

            public Slot(string label, string shortName, Func<string> adGroupIdSource, bool rewardRequired)
            {
                Label = label;
                ShortName = shortName;
                AdGroupIdSource = adGroupIdSource;
                RewardRequired = rewardRequired;
            }

            public string AdGroupId => AdGroupIdSource();

            public void DisposeLoad()
            {
                var unsub = UnsubscribeLoad;
                UnsubscribeLoad = null;
                SafeInvoke(unsub, Label, "load 구독 해제");
            }

            public void DisposeShow()
            {
                var unsub = UnsubscribeShow;
                UnsubscribeShow = null;
                SafeInvoke(unsub, Label, "show 구독 해제");
            }

            private static void SafeInvoke(Action action, string label, string what)
            {
                if (action == null) return;
                try { action(); }
                catch (Exception e) { Debug.LogWarning($"[Ads] {label} {what} 실패: {e.Message}"); }
            }
        }

        private readonly Slot _interstitial =
            new Slot("전면", "int", () => AdUnitIds.AitInterstitial, rewardRequired: false);

        private readonly Slot _rewarded =
            new Slot("리워드", "rwd", () => AdUnitIds.AitRewarded, rewardRequired: true);

        public bool IsInterstitialReady => _interstitial.IsReady;
        public bool IsRewardedReady     => _rewarded.IsReady;

        /// <summary>
        /// 지금 로드 요청이 나가 있는 슬롯. null 이면 로드 레인이 비어 있다.
        ///
        /// **광고 그룹 ID는 한 번에 하나씩만 로드해야 한다** — 앱인토스 인앱 광고 가이드가
        /// "반드시 1개씩 순차적으로 로드"를 요구하고, 콘솔 공지(51001·51183)도 "loaded 이벤트를
        /// 수신한 이후 다음 광고를 로드하라"고 못 박는다. 동시에 던지면 나중 요청이 앞 요청의
        /// 자리를 덮어써 loaded 이벤트가 유실되는데, 이 클래스는 loaded 로만 준비 여부를
        /// 판정하므로(SDK에 IsLoaded 조회가 없다) 그 광고는 세션 내내 준비되지 않는다.
        ///
        /// Android는 앱 5.267.0부터 복수 인스턴스를 지원해 이 규약을 어겨도 통과하지만,
        /// iOS에는 같은 개선이 공지되지 않았다. 양쪽 모두에서 성립하는 경로는 순차 로드뿐이다.
        /// </summary>
        private Slot _activeLoadSlot;

        /// <summary><see cref="_activeLoadSlot"/>의 로드 요청을 보낸 시각.</summary>
        private float _activeLoadStartedAt;

        /// <summary>
        /// 로드 응답을 이만큼 기다리고도 안 오면 레인을 포기하고 비운다.
        ///
        /// 앱인토스는 로드 이벤트 미전달 이슈를 두 번 공지했다(51001·51183). 이 SDK는 로드
        /// 실패를 err 콜백으로 알려주지만 **이벤트가 아예 오지 않는 경우**는 그 경로를 타지
        /// 않아서, 타임아웃이 없으면 레인이 영원히 잠기고 두 광고가 함께 죽는다.
        ///
        /// 90초는 문서가 밝힌 최대 네트워크 타임아웃(구글 애드몹 60초)에 여유를 둔 값이다.
        /// 정상 로드는 토스 애즈 1~2초, 애드몹 5~20초 안에 끝난다.
        /// </summary>
        private const float LoadTimeoutSeconds = 90f;

        // ── 초기화 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 별도의 SDK 초기화 호출은 없다. 첫 광고를 미리 로드하는 것이 전부다.
        ///
        /// **사전 로딩은 심사 요건이다.** 노출이 필요한 시점에 로드를 시작하면 반려 사유가 된다
        /// (AIT_PLAN.md P1-1). 그래서 여기서 한 번, 그리고 광고가 닫힐 때마다 다시 로드한다.
        ///
        /// 두 슬롯을 함께 예약하지만 실제로 나가는 요청은 하나다. 나머지는 앞 로드가 끝나면
        /// <see cref="PumpLoadQueue"/>가 이어서 보낸다.
        ///
        /// <b>리워드가 먼저다.</b> 순서가 곧 우선순위인 이유는 <see cref="PumpLoadQueue"/> 참고.
        /// </summary>
        public void Initialize()
        {
            // 기다리지 않는다 — 광고 로드가 진단 때문에 늦어지면 안 된다.
            ResolveOsTagAsync();

            RequestLoad(_rewarded);
            RequestLoad(_interstitial);
        }

        // ── 로드 ──────────────────────────────────────────────────────────────

        public void LoadInterstitial() => RequestLoad(_interstitial);
        public void LoadRewarded()     => RequestLoad(_rewarded);

        /// <summary>
        /// 로드를 예약한다. 레인이 비어 있으면 곧바로 나가고, 차 있으면 순서를 기다린다.
        /// </summary>
        private void RequestLoad(Slot slot)
        {
            // 이미 준비됐거나 요청이 떠 있으면 예약하지 않는다. 이 경로는 광고가 닫힐 때마다
            // 자동으로 들어오므로, 가드가 없으면 로드가 끝난 직후 같은 광고를 다시 로드한다.
            if (slot.IsReady || slot.IsLoading) return;

            slot.WantsLoad = true;
            PumpLoadQueue();
        }

        /// <summary>
        /// 레인이 비어 있으면 대기 중인 슬롯 하나를 골라 로드를 시작한다.
        ///
        /// <b>리워드가 전면보다 우선한다.</b> 레인이 하나뿐이라 앞선 로드가 끝나야 다음이
        /// 나가고, 앞선 로드가 응답 없이 멈추면 뒤는 타임아웃(90초)까지 통째로 대기한다.
        /// 그래서 어느 쪽을 앞에 두느냐가 "둘 중 하나만 산다면 무엇을 살릴 것인가"가 된다.
        ///
        /// 리워드(부활)는 <b>첫 게임오버부터</b> 필요하다. 반면 전면은 AdGatePolicy 가
        /// 누적 3판까지 막고(GracePeriodGames) 그 뒤로도 게임오버 3회마다 1회만 통과시킨다.
        /// 전면을 앞에 두면 한동안 쓰이지도 않을 광고가 레인을 잡고, 그것이 막히는 동안
        /// 당장 필요한 부활 광고가 죽는다.
        /// </summary>
        private void PumpLoadQueue()
        {
            if (IsLoadLaneBusy()) return;

            Slot next = IsLoadPending(_rewarded)     ? _rewarded
                      : IsLoadPending(_interstitial) ? _interstitial
                      : null;
            if (next == null) return;

            StartLoad(next);
        }

        /// <summary>예약돼 있고 아직 실제로 필요한(준비 안 됐고 요청도 안 떠 있는) 슬롯인가.</summary>
        private static bool IsLoadPending(Slot slot)
            => slot.WantsLoad && !slot.IsReady && !slot.IsLoading;

        /// <summary>
        /// 레인이 아직 응답을 기다리는 중인가. <see cref="LoadTimeoutSeconds"/>가 지났으면
        /// 응답을 포기하고 레인을 비운 뒤 false 를 돌려준다.
        ///
        /// 이 판정은 레인을 쓰려는 쪽이 들어올 때만 일어난다(폴링하지 않는다). 다음 광고를
        /// 띄우려는 시점에 풀리면 충분하고, 그 경로는 FinishSlot 이 항상 만들어 준다.
        /// </summary>
        private bool IsLoadLaneBusy()
        {
            if (_activeLoadSlot == null) return false;
            if (Time.realtimeSinceStartup - _activeLoadStartedAt < LoadTimeoutSeconds) return true;

            Slot stalled = _activeLoadSlot;
            _activeLoadSlot = null;

            // 세대를 올려 두면 뒤늦게 도착한 콜백이 이 슬롯 상태를 되살리지 못한다.
            stalled.LoadGeneration++;
            stalled.IsLoading = false;
            stalled.DisposeLoad();

            Debug.LogWarning($"[Ads] {stalled.Label} 로드 응답이 {LoadTimeoutSeconds}초 안에 오지 않아 레인을 비웁니다.");
            Track("to", stalled);
            return false;
        }

        private void StartLoad(Slot slot)
        {
            slot.WantsLoad = false;

            string adGroupId = slot.AdGroupId;
            if (string.IsNullOrEmpty(adGroupId))
            {
                Debug.LogWarning($"[Ads] {slot.Label} 광고 그룹 ID가 비어 있어 로드를 건너뜁니다.");

                // 이 슬롯은 레인을 잡지 않았으므로 대기 중인 다른 슬롯을 바로 태운다.
                // WantsLoad 를 이미 내렸으니 같은 슬롯으로 되돌아오지 않는다.
                PumpLoadQueue();
                return;
            }

            slot.IsLoading = true;
            slot.DisposeLoad();

            _activeLoadSlot = slot;
            _activeLoadStartedAt = Time.realtimeSinceStartup;
            int generation = ++slot.LoadGeneration;

            Track("req", slot);

            slot.UnsubscribeLoad = AIT.LoadFullScreenAd(
                adGroupId,
                e =>
                {
                    if (e == null || e.Type != "loaded") return;
                    if (slot.LoadGeneration != generation) return;

                    slot.IsLoading = false;
                    slot.IsReady = true;
                    Debug.Log($"[Ads] {slot.Label} 로드 완료");
                    Track("ok", slot);
                    ReleaseLoadLane(slot);
                },
                err =>
                {
                    if (slot.LoadGeneration != generation) return;

                    // 여기서 즉시 재시도하지 않는다 — 실패가 반복되면 무한 요청이 된다.
                    // 다음 기회는 "미준비 상태로 노출 시도" 경로가 만든다(FinishSlot의 finally).
                    slot.IsLoading = false;
                    slot.IsReady = false;
                    Debug.LogWarning($"[Ads] {slot.Label} 로드 실패: {err?.ErrorCode} {err?.Message}");
                    Track("err", slot, $"{err?.ErrorCode}");
                    ReleaseLoadLane(slot);
                });
        }

        /// <summary>로드 레인을 비우고, 기다리던 다음 로드를 태운다.</summary>
        private void ReleaseLoadLane(Slot slot)
        {
            if (_activeLoadSlot == slot) _activeLoadSlot = null;
            PumpLoadQueue();
        }

        // ── 노출 ──────────────────────────────────────────────────────────────

        /// <summary>
        /// onClosed(shown): shown=true 는 광고가 실제로 노출된 뒤 닫혔을 때만.
        /// 미준비·노출 실패는 게임 흐름을 막지 않도록 즉시 false 로 돌려준다.
        /// </summary>
        public void ShowInterstitial(Action<bool> onClosed = null) => ShowSlot(_interstitial, onClosed);

        /// <summary>onResult(true) = 보상 획득. <b>중도 이탈은 false.</b></summary>
        public void ShowRewarded(Action<bool> onResult) => ShowSlot(_rewarded, onResult);

        private void ShowSlot(Slot slot, Action<bool> callback)
        {
            slot.Callback = callback;
            slot.Succeeded = false;
            slot.Finished = false;
            slot.ImpressionTracked = false;

            if (!slot.IsReady)
            {
                // 광고가 없을 뿐이므로 게임은 그대로 진행시킨다. 노출 없음 → false.
                // FinishSlot 이 재로드까지 책임진다.
                Track("skip", slot);
                FinishSlot(slot, false);
                return;
            }

            // 노출과 동시에 소비된다. dismissed 이후에는 다시 Load 해야 한다(SDK 규약).
            slot.IsReady = false;
            slot.DisposeShow();

            // 광고가 화면을 덮는 동안 게임 소리가 새어 나가지 않게 한다(심사 요건).
            // 해제는 FinishSlot 한 곳에서만 한다 — 종료 경로가 여럿이라 여기서 짝을 맞추면
            // 반드시 하나를 빠뜨린다.
            SetAudioSuspended(true);

            slot.UnsubscribeShow = AIT.ShowFullScreenAd(
                slot.AdGroupId,
                e =>
                {
                    switch (e?.Type)
                    {
                        case "impression":
                        case "show":
                            // 전면의 성공 판정 근거. 실제로 화면에 떴다는 뜻이다.
                            if (!slot.RewardRequired) slot.Succeeded = true;

                            if (!slot.ImpressionTracked)
                            {
                                slot.ImpressionTracked = true;
                                Track("imp", slot);
                            }
                            break;

                        case "userEarnedReward":
                            // **보상은 이 이벤트에서만 지급한다.** dismissed 만 보고 주면 정책 위반이다.
                            if (slot.RewardRequired) slot.Succeeded = true;
                            break;

                        case "failedToShow":
                            Debug.LogWarning($"[Ads] {slot.Label} 노출 실패(failedToShow)");
                            FinishSlot(slot, false);
                            break;

                        case "dismissed":
                            FinishSlot(slot, slot.Succeeded);
                            break;
                    }
                },
                err =>
                {
                    Debug.LogWarning($"[Ads] {slot.Label} 노출 오류: {err?.ErrorCode} {err?.Message}");
                    FinishSlot(slot, false);
                });
        }

        /// <summary>
        /// 결과를 정확히 한 번 통보하고 다음 광고를 준비한다.
        ///
        /// 한 번만 호출되도록 막는 이유: failedToShow 뒤에 dismissed 가 따라오는 등
        /// 종료 이벤트가 겹쳐 도착할 수 있다. 두 번 부르면 게임 흐름이 이중으로 진행된다
        /// (게임오버 팝업이 두 번 닫히거나 부활이 두 번 지급되는 형태).
        /// </summary>
        private void FinishSlot(Slot slot, bool result)
        {
            if (slot.Finished) return;
            slot.Finished = true;

            var callback = slot.Callback;
            slot.Callback = null;
            slot.DisposeShow();

            // 노출을 시도하지 않은 경로(미준비)로도 들어오지만, 그때는 애초에 건 적이 없어
            // 해제가 무해하다. 짝을 맞추려 조건을 붙이면 오히려 빠뜨리는 경로가 생긴다.
            SetAudioSuspended(false);

            // 게임 콜백이 던져도 재로드는 반드시 한다. 이 호출은 게임오버 팝업·부활·세션 저장까지
            // 이어지므로 예외가 나갈 수 있고, 여기서 스킵되면 앱을 다시 켤 때까지 광고가 죽는다.
            // (AdMobProvider가 같은 이유로 finally 를 쓴다)
            try
            {
                callback?.Invoke(result);
            }
            finally
            {
                RequestLoad(slot);
            }
        }

        // ── 진단 ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 진단 로그 이름에 박을 OS 구분자.
        ///
        /// 콘솔 이벤트 로그는 파라미터로 필터링해 보여주지 않는다 — 구분해야 할 축은
        /// 전부 로그 이름에 담아야 조회된다. WebGL 빌드는 하나라서 전처리기로는 OS를
        /// 가릴 수 없다.
        ///
        /// ⚠ 예전에는 <c>SystemInfo.operatingSystem</c> 을 파싱했는데, WebGL 에서는 이 값이
        ///   "iPhone"/"Android" 를 담고 있지 않아 **모든 기기가 etc 로 찍혔다.** 2026-08-19
        ///   콘솔 실측에서 iOS 14명·안드로이드 20명이 들어왔는데도 <c>ad_*_etc</c> 만 쌓여,
        ///   iOS 판정이라는 이 진단의 본래 목적이 그대로 무산됐다.
        ///   토스가 주는 <see cref="AIT.GetPlatformOS"/> 로 바꾼다.
        /// </summary>
        private static string OsTag = "pnd";   // pending — 아직 판별 전

        /// <summary>
        /// OS 판별을 시작한다. <see cref="AIT.GetPlatformOS"/> 가 비동기라 즉시 값이 나오지
        /// 않으므로, 판별이 끝나기 전에 나간 로그는 <c>pnd</c> 로 남는다.
        ///
        /// **광고 로드를 여기에 기다리게 하지 않는다.** 진단 때문에 광고가 늦어지면 본말전도다.
        /// pnd 가 많이 쌓이면 그건 판별이 늦다는 뜻이고, 그 자체가 읽을 수 있는 신호다.
        /// </summary>
        private static async void ResolveOsTagAsync()
        {
            try
            {
                string os = await AIT.GetPlatformOS() ?? string.Empty;

                if (os.IndexOf("ios", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    os.IndexOf("iphone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    os.IndexOf("ipad", StringComparison.OrdinalIgnoreCase) >= 0)
                    OsTag = "ios";
                else if (os.IndexOf("android", StringComparison.OrdinalIgnoreCase) >= 0)
                    OsTag = "and";
                else
                    // 판별 실패를 ios/and 어느 쪽으로도 밀어 넣지 않는다. 잘못 섞이면 지표가
                    // 오염되고, 그 오염은 이 로그를 믿고 내리는 판단 전체를 망친다.
                    // 값을 받긴 했으나 못 알아본 경우라 pnd(미도착)와는 구분해 둔다.
                    OsTag = "etc";
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Ads] OS 판별 실패: {e.Message}");
                OsTag = "err";
            }
        }

        /// <summary>
        /// 광고 파이프라인의 한 단계를 콘솔 이벤트 로그로 남긴다.
        /// 콘솔에는 <c>ad_{stage}_{slot}_{os}::impression</c> 이름으로 잡힌다.
        ///
        /// iOS 실기기가 없어 로컬에서 재현할 수 없는 증상(출시 후 iOS 광고 노출 0건)을
        /// 원격에서 판정하기 위한 것이다. 읽는 법:
        /// - req 는 있는데 ok·err 가 둘 다 없다 → 로드 이벤트 미전달
        /// - err 가 있다 → detail 의 에러 코드가 원인을 지목
        /// - skip 만 쌓인다 → 준비된 광고가 없어 매번 건너뛰는 중
        /// - req 자체가 없다 → 광고 이전에 게임이 그 지점까지 못 간 것
        /// </summary>
        private static void Track(string stage, Slot slot, string detail = null)
        {
            var payload = new Dictionary<string, object>
            {
                ["log_name"] = $"ad_{stage}_{slot.ShortName}_{OsTag}",
                ["slot"] = slot.Label,
            };
            if (!string.IsNullOrEmpty(detail)) payload["detail"] = detail;

            SendTrack(payload);
        }

        /// <summary>
        /// 전송은 기다리지 않는다. 진단이 광고 흐름을 늦추거나 막으면 본말전도다.
        /// async void 지만 본문 전체가 try 로 감싸여 있어 예외가 밖으로 새지 않는다.
        /// </summary>
        private static async void SendTrack(Dictionary<string, object> payload)
        {
            try
            {
                await AIT.AnalyticsImpression(payload);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Ads] 진단 로그 전송 실패: {e.Message}");
            }
        }

        // ── 동의 철회 대응 ────────────────────────────────────────────────────

        /// <summary>
        /// 광고 표시 중 소리 정지. AudioManager가 없거나 예외가 나도 광고 흐름은 계속돼야 한다.
        /// </summary>
        private static void SetAudioSuspended(bool suspended)
        {
            try
            {
                if (AudioManager.IsValidInstance())
                    AudioManager.Instance.SetAdSuspended(suspended);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Ads] 소리 정지 처리 실패: {e.Message}");
            }
        }

        /// <summary>
        /// 앱인토스에는 앱이 관리하는 동의 철회 개념이 없으므로 할 일이 없다.
        ///
        /// 버리는 쪽이 안전해 보이지만 그 반대다. 이 경로에는 재로드 트리거가 없어서,
        /// 여기서 준비된 광고를 버리면 다음 노출 시도까지 광고가 비어 있게 된다.
        /// WebGL 경로의 AdManager는 동의 이벤트를 구독하지 않으므로 실제로 호출되지도 않는다.
        /// </summary>
        public void DiscardLoadedAds() { }
    }
}
#endif
