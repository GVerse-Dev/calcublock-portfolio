using System;
using UniRx;
using IGMain;
#if UNITY_ANDROID
using GooglePlayGames;
using GooglePlayGames.BasicApi;
#endif

public class GpgsSignInService : ISignInService
{
    // 콜백이 오지 않을 때 InProgress에서 빠져나오기 위한 상한.
    //
    // GPGS는 인증 요청을 취소할 방법을 제공하지 않는다. 콜백이 유일한 탈출구인데,
    // Play 서비스 업데이트 중이거나 네트워크가 끊기거나 사용자가 계정 선택 창을
    // 강제 종료하면 콜백이 영영 오지 않는다. 그러면 상태가 InProgress에 고착되고,
    // MainPanel.ToggleGPGS가 InProgress를 눌러도 무시하므로 로그인 버튼이
    // 앱을 재시작할 때까지 영구 무반응이 된다. 사용자에겐 "버튼이 안 눌린다"로만 보인다.
    //
    // 값 선정:
    //   Silent - 사용자 상호작용이 없다. 네트워크·Play 서비스 지연만 감안하면 된다.
    //   Manual - 구글 계정 선택 UI가 앞에 뜨고 사용자가 고르는 시간이 필요하다.
    //            너무 짧으면 사용자가 아직 고르는 중에 발동한다. 그래도 결과가
    //            틀어지지는 않는다(아래 참고) — 다만 불필요한 상태 전이가 생긴다.
    private static readonly TimeSpan SilentTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ManualTimeout = TimeSpan.FromSeconds(60);

    private readonly ReactiveProperty<SignInState> _state =
        new ReactiveProperty<SignInState>(SignInState.Unknown);

    private IDisposable _timeout;
    private bool _disposed;

    public IReadOnlyReactiveProperty<SignInState> State => _state;
    public string PlayerId { get; private set; } = string.Empty;
    public string PlayerDisplayName { get; private set; } = string.Empty;

    public GpgsSignInService()
    {
        // Observable.Timer가 메인 스레드 스케줄러를 쓰므로 디스패처를 미리 깨워둔다.
        // (AdMainThread.EnsureInitialized와 같은 이유)
        MainThreadDispatcher.Initialize();

#if UNITY_ANDROID
        PlayGamesPlatform.Activate();
#endif
    }

    public void AuthenticateSilently()
    {
#if UNITY_ANDROID
        BeginAuthentication(SilentTimeout);
        PlayGamesPlatform.Instance.Authenticate(OnSignInResult);
#else
        _state.Value = SignInState.SignedOut;
#endif
    }

    public void AuthenticateManually()
    {
#if UNITY_ANDROID
        BeginAuthentication(ManualTimeout);
        PlayGamesPlatform.Instance.ManuallyAuthenticate(OnSignInResult);
#else
        _state.Value = SignInState.SignedOut;
#endif
    }

    // ── 타임아웃 ─────────────────────────────────────────────────────────────

    private void BeginAuthentication(TimeSpan timeout)
    {
        _state.Value = SignInState.InProgress;

        _timeout?.Dispose();

        // 스케줄러를 반드시 명시할 것. UniRx의 기본 스케줄러(Scheduler.MainThread)는
        // 내부적으로 WaitForSeconds를 쓰므로 Time.timeScale에 스케일된다.
        // timeScale이 0이면(일시정지) 타이머가 영영 발화하지 않아,
        // 이 코드가 고치려는 고착 버그를 그대로 재현하게 된다.
        // 타임아웃은 게임 시간이 아니라 실제 시간 기준이어야 한다.
        _timeout = Observable
            .Timer(timeout, Scheduler.MainThreadIgnoreTimeScale)
            .Subscribe(_ => OnTimeout());
    }

    /// <summary>
    /// 콜백이 제때 오지 않았다. SignedOut으로 되돌려 버튼을 다시 누를 수 있게 한다.
    ///
    /// GPGS 호출 자체는 취소할 수 없으므로 요청은 그대로 살아 있다.
    /// 뒤늦게 콜백이 도착하면 OnSignInResult가 정상적으로 상태를 확정한다 —
    /// 즉 타임아웃이 일찍 발동해도 최종 결과는 틀어지지 않는다.
    /// </summary>
    private void OnTimeout()
    {
        _timeout = null;

        if (_disposed) return;
        if (_state.Value != SignInState.InProgress) return;

        // 릴리스에서도 보여야 하는 이상 상황이다. IGLog.Verbose는 컴파일 단계에서
        // 삭제되므로 현장 로그캣 진단에 쓸 수 없다.
        UnityEngine.Debug.LogWarning(
            "[SignIn] GPGS 콜백이 오지 않아 타임아웃. SignedOut으로 복구합니다.");

        PlayerId = string.Empty;
        PlayerDisplayName = string.Empty;
        _state.Value = SignInState.SignedOut;
    }

    private void CancelTimeout()
    {
        _timeout?.Dispose();
        _timeout = null;
    }

    // ── GPGS 콜백 ────────────────────────────────────────────────────────────

#if UNITY_ANDROID
    private void OnSignInResult(SignInStatus status)
    {
        CancelTimeout();

        if (_disposed) return;

        IGLog.Verbose($"[SignIn] GPGS result: {status}");
        if (status == SignInStatus.Success)
        {
            PlayerId = PlayGamesPlatform.Instance.GetUserId();
            PlayerDisplayName = PlayGamesPlatform.Instance.GetUserDisplayName();
            _state.Value = SignInState.SignedIn;
        }
        else
        {
            PlayerId = string.Empty;
            PlayerDisplayName = string.Empty;
            _state.Value = SignInState.SignedOut;
        }
    }
#endif

    public void Dispose()
    {
        _disposed = true;
        CancelTimeout();
        _state.Dispose();
    }
}
