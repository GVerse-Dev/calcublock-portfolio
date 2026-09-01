using UniRx;

public class SignInManager : ManagerBase<SignInManager>
{
    private ISignInService _service;
    private bool _initialized;

    public IReadOnlyReactiveProperty<SignInState> State => _service?.State;
    public string PlayerDisplayName => _service?.PlayerDisplayName ?? string.Empty;

    /// <summary>
    /// 플랫폼이 준 사용자 식별자. 미로그인·미지원 환경에서는 빈 문자열.
    ///
    /// 앱인토스에서는 미니앱별 고유·불변인 익명 해시키이며, 세이브 데이터의 소유자
    /// 식별자로 쓴다 (AIT_PLAN.md P1-3).
    /// </summary>
    public string PlayerId => _service?.PlayerId ?? string.Empty;

    public override void InitializeManager()
    {
        if (_initialized) return;
        _initialized = true;

        // WebGL 분기가 Android보다 먼저다. 앱인토스 빌드에서도 UNITY_ANDROID가 정의되는
        // 조합을 피하기 위한 순서가 아니라, 플랫폼별 서비스를 한눈에 읽히게 두기 위함이다.
        //
        // AitSignInService는 에디터(WebGL 타깃)에서도 쓴다. SDK가 mock으로 빈 키를 돌려주고
        // 서비스는 그것을 SignedOut으로 처리하므로 NullSignInService와 동작이 같다.
        // 대신 실제 경로가 에디터에서도 실행되어 배선 오류가 빌드 전에 드러난다.
#if UNITY_WEBGL
        _service = new AitSignInService();
#elif UNITY_ANDROID && !UNITY_EDITOR
        _service = new GpgsSignInService();
#else
        _service = new NullSignInService();
#endif
        _service.AuthenticateSilently();
    }

    public void RequestManualSignIn()
    {
        _service?.AuthenticateManually();
    }

    public override void ClearManager() { }

    public override void FinalizeManager()
    {
        _service?.Dispose();
        _service = null;
        _initialized = false;
    }
}
