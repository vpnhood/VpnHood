namespace VpnHood.Client.App.Abstractions;

public interface IAppAuthenticationService : IDisposable
{
    bool IsSignInWithGoogleSupported { get; }
    Task SignInWithGoogle();
    Task SignOut();
}