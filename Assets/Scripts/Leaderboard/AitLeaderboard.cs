#if UNITY_WEBGL
using System;
using System.Globalization;
using AppsInToss;
using UnityEngine;

namespace IGMain.Leaderboard
{
    /// <summary>
    /// 앱인토스 게임 리더보드.
    ///
    /// 토스가 점수 저장·랭킹 화면·사용자 식별을 전부 자기 쪽에서 처리한다. 우리는 점수를
    /// 밀어 넣고(<see cref="SubmitBest"/>) 랭킹 화면을 열어 달라고 요청할(<see cref="Open"/>)
    /// 뿐이다. **읽기 API가 없다** — 순위를 게임 안에 직접 그릴 수는 없고, 토스가 띄우는
    /// WebView로만 보여줄 수 있다.
    ///
    /// 토스 로그인은 필요 없다. 미니앱은 이미 로그인된 토스 앱 안에서 돌고 토스가 내부적으로
    /// 사용자를 알아본다. 그래서 응답에 사용자 식별자가 담기지 않는다("보안상의 이유로,
    /// 사용자 식별자는 응답에 포함되지 않아요"). 토스는 누군지 알고 우리만 모르는 구조다.
    ///
    /// 동작 전제 (공식 문서 기준):
    /// - **미니앱 정보 승인 완료** 후에만 동작한다. 승인 전에는 <c>LEADERBOARD_NOT_FOUND</c>.
    /// - 사용자에게 **토스게임센터 프로필**이 있어야 한다. 없으면 <c>PROFILE_NOT_FOUND</c>.
    ///   우리가 붙일 수 있는 것이 아니라 토스 쪽 프로필이다.
    /// - 토스앱 5.221.0 이상. 미만이면 브릿지 응답이 오지 않아 결과가 null이다.
    /// - 미니앱당 리더보드는 **1개뿐**이라 주간·모드별 분리가 불가능하다.
    /// - 샌드박스에서 낸 점수는 실서비스 리더보드에 반영되지 않는다.
    /// - 점수 검증이 없다. 클라이언트가 보낸 숫자를 그대로 받는다.
    ///
    /// 실패는 전부 삼킨다. 랭킹은 부가 기능이고 게임오버 흐름이 이것 때문에 막히면 안 된다.
    /// </summary>
    public static class AitLeaderboard
    {
        /// <summary>
        /// 무기한 대기(0)는 쓰지 않는다. 응답이 영영 오지 않으면 AITCore 콜백 테이블에
        /// 항목이 영구히 남는다 — <see cref="AitSignInService"/>와 같은 이유다.
        /// </summary>
        private const int TIMEOUT_MS = 10_000;

        /// <summary>
        /// 이번 실행에서 마지막으로 제출한 점수. 최고 기록이 갱신되지 않은 게임오버에서
        /// 같은 값을 반복 제출하지 않기 위한 것이다(부활 때문에 한 판에 두 번 올 수 있다).
        /// </summary>
        private static long _lastSubmitted = -1;

        /// <summary>제출이 떠 있는 동안 또 부르지 않는다. 응답 순서가 뒤집히는 것을 막는다.</summary>
        private static bool _submitting;

        /// <summary>
        /// 최고 점수를 제출한다. **이번 판 점수가 아니라 개인 최고 기록을 보낸다.**
        ///
        /// 토스가 제출값 중 최대를 남기는지 최신을 남기는지 문서에 없다. 최신을 남긴다면
        /// 낮은 점수로 끝난 판이 순위를 끌어내리므로, 애초에 단조 증가하는 값만 보내
        /// 어느 쪽이든 결과가 같게 만든다. 랭킹에 걸리는 값도 어차피 최고 기록이다.
        /// </summary>
        public static void SubmitBest(long bestScore)
        {
            if (bestScore <= 0) return;

            // 갱신되지 않았으면 보낼 이유가 없다.
            if (bestScore <= _lastSubmitted) return;

            if (_submitting) return;

            Submit(bestScore);
        }

        private static async void Submit(long score)
        {
            _submitting = true;

            try
            {
                // 문화권 무관 표기. 문서가 요구하는 형식은 "실수 형태의 숫자 문자열"이고,
                // 지역 설정에 따라 자릿수 구분자가 끼면 UNPARSABLE_SCORE가 된다.
                string payload = score.ToString(CultureInfo.InvariantCulture);

                var response = await AIT.SubmitGameCenterLeaderBoardScore(
                    new SubmitGameCenterLeaderBoardScoreParams { Score = payload },
                    TIMEOUT_MS);

                // 에디터 mock과 구버전 토스앱(브릿지 응답 없음) 모두 null로 온다.
                if (response == null)
                {
                    Debug.LogWarning("[Leaderboard] 점수 제출 응답이 없다 (에디터 mock이거나 토스앱 5.221.0 미만).");
                    return;
                }

                if (response.StatusCode == "SUCCESS")
                {
                    _lastSubmitted = score;
                    Debug.Log($"[Leaderboard] 점수 제출 완료: {payload}");
                    return;
                }

                // 실패해도 _lastSubmitted를 올리지 않는다 — 다음 게임오버에서 다시 시도된다.
                // LEADERBOARD_NOT_FOUND(앱 정보 미승인) / PROFILE_NOT_FOUND(게임센터 프로필 없음)
                // / UNPARSABLE_SCORE 가 문서에 있는 전부다. 인증 실패 계열은 없다.
                Debug.LogWarning($"[Leaderboard] 점수 제출 실패: {response.StatusCode}");
            }
            catch (Exception e)
            {
                // 타임아웃(AITClientTimeoutException) 포함. async void라 여기서 막지 않으면
                // 예외가 그대로 사라지고 원인을 알 수 없게 된다.
                Debug.LogWarning($"[Leaderboard] 점수 제출 예외: {e.GetType().Name} - {e.Message}");
            }
            finally
            {
                _submitting = false;
            }
        }

        /// <summary>
        /// 토스 랭킹 화면(WebView)을 연다. 우리 UI가 아니라 토스가 그리는 화면이다.
        ///
        /// 문서가 "게임 진입 직후가 아니라 플레이가 끝난 뒤에 열라"고 안내하므로,
        /// 사용자가 직접 누른 버튼에서만 호출한다.
        /// </summary>
        public static async void Open()
        {
            try
            {
                await AIT.OpenGameCenterLeaderboard(TIMEOUT_MS);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Leaderboard] 랭킹 화면 열기 실패: {e.GetType().Name} - {e.Message}");
            }
        }
    }
}
#endif
