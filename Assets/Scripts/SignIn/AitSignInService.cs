#if UNITY_WEBGL
using System;
using AppsInToss;
using UniRx;
using UnityEngine;

/// <summary>
/// 앱인토스 사용자 식별.
///
/// GPGS와 달리 **사용자가 거치는 로그인 절차가 없다.** 토스가 미니앱별로 고유·불변인
/// 익명 해시키를 발급해 주고, 우리는 그것을 받아오기만 한다. 서버 인증도 필요 없다.
/// 그래서 <see cref="AuthenticateManually"/>가 <see cref="AuthenticateSilently"/>와 같다.
///
/// 이 키의 용도는 **세이브 데이터의 소유자 식별자**다. WebGL의 로컬 저장소(IndexedDB)는
/// 토스 웹뷰가 언제 비울지 보장이 없어서, 기기가 아니라 사용자에 묶인 식별자가 있어야
/// 원격 저장본을 되찾을 수 있다 (AIT_PLAN.md P1-3).
///
/// ⚠ 토스 앱 밖(일반 브라우저·에디터)에서는 빈 문자열이 온다. 실패가 아니라 정상이며,
/// 그 경우 <see cref="SignInState.SignedOut"/>으로 두어 호출부가 기존 로그아웃 경로를
/// 그대로 타게 한다.
/// </summary>
public class AitSignInService : ISignInService
{
    /// <summary>
    /// 무기한 대기(0)는 쓰지 않는다. 응답이 영영 오지 않으면 상태가 InProgress에 묶여
    /// UI가 "로그인 중"에서 멈춘다. 초기 기동 경로라 넉넉히 준다.
    /// </summary>
    private const int TIMEOUT_MS = 10_000;

    private readonly ReactiveProperty<SignInState> _state =
        new ReactiveProperty<SignInState>(SignInState.Unknown);

    private string _userKey = string.Empty;
    private bool _disposed;

    public IReadOnlyReactiveProperty<SignInState> State => _state;

    /// <summary>미니앱별 익명 해시키. 조회 전이거나 토스 앱 밖이면 빈 문자열.</summary>
    public string PlayerId => _userKey;

    /// <summary>익명 키라 표시할 이름이 없다.</summary>
    public string PlayerDisplayName => string.Empty;

    public void AuthenticateSilently() => Fetch();

    /// <summary>대화형 로그인 절차가 없으므로 조용한 인증과 동일하다(실패 후 재시도용).</summary>
    public void AuthenticateManually() => Fetch();

    private async void Fetch()
    {
        if (_disposed) return;

        // 이미 받았거나 요청이 떠 있으면 중복 호출하지 않는다.
        if (_state.Value == SignInState.InProgress || _state.Value == SignInState.SignedIn) return;

        _state.Value = SignInState.InProgress;

        try
        {
            string key = await AIT.GetUserKeyForGame(TIMEOUT_MS);

            // await 사이에 매니저가 정리됐을 수 있다. Dispose된 ReactiveProperty에
            // 값을 넣으면 예외가 나가는데, async void라 그 예외는 조용히 사라진다.
            if (_disposed) return;

            if (string.IsNullOrEmpty(key))
            {
                // 토스 앱 밖에서 실행 중이다. 실패가 아니라 이 환경의 정상 결과다.
                _userKey = string.Empty;
                _state.Value = SignInState.SignedOut;
                return;
            }

            _userKey = key;
            _state.Value = SignInState.SignedIn;

            // 키 전체는 남기지 않는다. 익명이라도 사용자 단위 식별자이고, 로그는
            // 콘솔·크래시 리포트로 흘러간다. 존재 여부 확인에는 길이면 충분하다.
            Debug.Log($"[SignIn] 앱인토스 사용자 키 확보 (길이 {key.Length})");
        }
        catch (Exception e)
        {
            if (_disposed) return;

            // 타임아웃(AITClientTimeoutException) 포함. 게임은 그대로 진행돼야 하므로
            // 로그아웃 상태로 두고 끝낸다 — 재시도는 호출부(RequestManualSignIn)가 정한다.
            Debug.LogWarning($"[SignIn] 사용자 키 조회 실패: {e.GetType().Name} - {e.Message}");
            _userKey = string.Empty;
            _state.Value = SignInState.SignedOut;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _state.Dispose();
    }
}
#endif
