using System;
using UniRx;

public interface ISignInService : IDisposable
{
    IReadOnlyReactiveProperty<SignInState> State { get; }
    string PlayerId { get; }
    string PlayerDisplayName { get; }
    void AuthenticateSilently();
    void AuthenticateManually();
}
