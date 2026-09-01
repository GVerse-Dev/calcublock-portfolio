using UniRx;

public class NullSignInService : ISignInService
{
    private readonly ReactiveProperty<SignInState> _state =
        new ReactiveProperty<SignInState>(SignInState.SignedOut);

    public IReadOnlyReactiveProperty<SignInState> State => _state;
    public string PlayerId => string.Empty;
    public string PlayerDisplayName => string.Empty;

    public void AuthenticateSilently() { }
    public void AuthenticateManually() { }

    public void Dispose() => _state.Dispose();
}
