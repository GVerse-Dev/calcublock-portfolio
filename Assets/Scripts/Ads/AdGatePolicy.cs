using System;
using UnityEngine;

namespace IGMain.Ads
{
    /// <summary>
    /// 전면 광고 노출 정책 (frequency capping).
    /// mechanism(AdManager/Provider)과 분리된 순수 C# 클래스 — MonoBehaviour 아님.
    /// AdManager가 인스턴스를 소유하며, 게임 코드는 AdManager를 통해서만 접근한다.
    /// </summary>
    internal sealed class AdGatePolicy
    {
        // ── 튜닝 파라미터 — 여기서만 조정 ──────────────────────────────────────
        private const int   GracePeriodGames               = 3;    // 누적 플레이 이하: 신규 유저 보호
        private const int   InterstitialEveryNGameOvers    = 3;    // 게임오버 N회마다 전면 1회
        private const float MinSecondsBetweenInterstitials = 90f;  // 전면 간 최소 경과 시간

        // ── 영속 상태 키 ─────────────────────────────────────────────────────
        //
        // **PlayerPrefs를 쓴다. gameData.json이 아니다.**
        // 세이브 파일은 외부 앱전용 저장소(/storage/emulated/0/Android/data/<pkg>/files)에 있어
        // 루팅 없이 adb push로 교체할 수 있지만, PlayerPrefs는 내부 저장소
        // (/data/data/<pkg>/shared_prefs)라 루팅이 필요하다. 이 값들의 존재 이유가
        // "전면 광고 회피를 막는 것"이므로 변조 문턱이 높은 쪽에 둔다.
        private const string PrefGameOvers    = "AdGate.GameOversSinceInterstitial";
        private const string PrefLastShownUtc = "AdGate.LastInterstitialUtcTicks";

        // ── 상태 ─────────────────────────────────────────────────────────────
        //
        // 게임오버 카운터와 마지막 노출 시각은 **앱 재시작을 넘어 유지된다.**
        // 예전에는 세션 메모리였는데, 그러면 매판 앱을 종료하는 것만으로 카운터가 0으로
        // 돌아가 전면 광고를 도구 없이 영구 회피할 수 있었다. 조작 의도가 없는 사용자도
        // 백그라운드 프로세스 회수로 같은 일이 벌어져 노출 빈도가 설계보다 낮았다.
        private int _gameOversSinceLastInterstitial;

        // Time.realtimeSinceStartup은 프로세스 수명에 묶여 있어 재시작하면 0으로 돌아간다.
        // 실제 경과 시간을 재려면 벽시계가 필요하다. null = 아직 한 번도 노출 안 함.
        private DateTime? _lastInterstitialUtc;

        // 이건 세션 범위가 맞다. 부활 광고 면제는 그 판 안에서만 의미가 있고,
        // 영속화하면 사용자에게 유리한 면제를 재시작 후에도 들고 다니게 된다.
        private bool _justWatchedRewarded;

        public AdGatePolicy()
        {
            _gameOversSinceLastInterstitial =
                Mathf.Clamp(PlayerPrefs.GetInt(PrefGameOvers, 0), 0, 9999);

            _lastInterstitialUtc = ReadLastShownUtc();
        }

        /// <summary>
        /// 저장된 마지막 노출 시각을 읽는다. 값이 없거나 신뢰할 수 없으면 null.
        ///
        /// 시스템 시계를 앞으로 돌려 두면 "미래에 마지막 광고를 봤다"는 상태가 되어
        /// 경과 시간이 영원히 음수로 남는다. 그런 기록은 버려서 즉시 만료 처리한다.
        /// </summary>
        private static DateTime? ReadLastShownUtc()
        {
            string raw = PlayerPrefs.GetString(PrefLastShownUtc, string.Empty);
            if (string.IsNullOrEmpty(raw)) return null;
            if (!long.TryParse(raw, out long ticks)) return null;
            if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks) return null;

            var stored = new DateTime(ticks, DateTimeKind.Utc);
            return stored > DateTime.UtcNow ? null : stored;
        }

        // ── 상태 통지 메서드 ──────────────────────────────────────────────────

        /// <summary>진짜 게임오버가 확정됐을 때 호출. 게임오버 카운터를 증가시킨다.</summary>
        public void NotifyGameOver()
        {
            if (_gameOversSinceLastInterstitial < 9999)
                _gameOversSinceLastInterstitial++;

            // 즉시 디스크에 반영한다. 강제 종료로 카운터가 날아가는 것을 막는 것이 이 저장의 목적이라
            // 다음 자동 저장 시점(일시정지·종료)까지 미루면 의미가 없다.
            PlayerPrefs.SetInt(PrefGameOvers, _gameOversSinceLastInterstitial);
            PlayerPrefs.Save();
        }

        /// <summary>마지막 노출 기록을 폐기한다. 시계가 신뢰할 수 없을 때 쓴다.</summary>
        private void ForgetLastShown()
        {
            _lastInterstitialUtc = null;
            PlayerPrefs.DeleteKey(PrefLastShownUtc);
            PlayerPrefs.Save();
        }

        /// <summary>리워드 광고를 완주(success=true)했을 때 호출. 다음 전면 광고 1회를 면제한다.</summary>
        public void NotifyRewardedShown()
        {
            _justWatchedRewarded = true;
        }

        /// <summary>전면 광고를 실제로 노출했을 때 호출. 카운터를 리셋하고 타이머를 갱신한다.</summary>
        public void NotifyInterstitialShown()
        {
            _lastInterstitialUtc            = DateTime.UtcNow;
            _gameOversSinceLastInterstitial = 0;
            _justWatchedRewarded            = false;

            PlayerPrefs.SetInt(PrefGameOvers, 0);
            PlayerPrefs.SetString(PrefLastShownUtc, _lastInterstitialUtc.Value.Ticks.ToString());
            PlayerPrefs.Save();
        }

        // ── 판정 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 전면 광고를 지금 노출해도 되는지 판단한다.
        /// false 반환 시에도 _justWatchedRewarded 플래그는 소비(해제)된다.
        /// </summary>
        public bool ShouldShowInterstitial()
        {
            // 1. 신규 유저 보호: 누적 플레이 수가 grace period 이하
            if (SaveManager.IsValidInstance() &&
                SaveManager.Instance.CurrentSaveData.TotalGamesPlayed <= GracePeriodGames)
                return false;

            // 2. 직전에 부활 리워드 광고를 봤으면 이번 전면 1회 면제, 플래그 소비
            if (_justWatchedRewarded)
            {
                _justWatchedRewarded = false;
                return false;
            }

            // 3. 마지막 전면 이후 최소 경과 시간 미달
            //    (한 번도 노출한 적이 없으면 이 조건은 통과시킨다)
            if (_lastInterstitialUtc.HasValue)
            {
                double elapsed = (DateTime.UtcNow - _lastInterstitialUtc.Value).TotalSeconds;

                if (elapsed < 0)
                {
                    // 벽시계가 뒤로 조정됐다. 기록이 미래에 있으면 경과 시간이 영원히 음수라
                    // 이 조건이 항상 참이 되어 **그 세션 내내 전면 광고가 봉인된다.**
                    // 생성자의 ReadLastShownUtc에도 같은 방어가 있지만 그건 앱 시작 시점만 본다 —
                    // 노출한 뒤에 시계가 바뀌면 재시작 전까지 걸리지 않는다.
                    // 신뢰할 수 없는 기록이므로 버리고 즉시 만료 처리한다.
                    ForgetLastShown();
                }
                else if (elapsed < MinSecondsBetweenInterstitials)
                {
                    return false;
                }
            }

            // 4. 게임오버 횟수 미달
            if (_gameOversSinceLastInterstitial < InterstitialEveryNGameOvers)
                return false;

            return true;
        }

#if UNITY_EDITOR || DEBUG_ADS
        /// <summary>[디버그 전용] 정책 상태를 초기화한다.</summary>
        public void DebugReset()
        {
            _gameOversSinceLastInterstitial = 0;
            _lastInterstitialUtc            = null;
            _justWatchedRewarded            = false;

            PlayerPrefs.DeleteKey(PrefGameOvers);
            PlayerPrefs.DeleteKey(PrefLastShownUtc);
            PlayerPrefs.Save();
        }

        public string DebugStatus =>
            $"gameOvers={_gameOversSinceLastInterstitial}/{InterstitialEveryNGameOvers}  " +
            $"elapsed={(_lastInterstitialUtc.HasValue ? (DateTime.UtcNow - _lastInterstitialUtc.Value).TotalSeconds : double.PositiveInfinity):F0}s/{MinSecondsBetweenInterstitials}s  " +
            $"rewardedFlag={_justWatchedRewarded}  " +
            $"gamesPlayed={( SaveManager.IsValidInstance() ? SaveManager.Instance.CurrentSaveData.TotalGamesPlayed.ToString() : "?")}";
#endif
    }
}
