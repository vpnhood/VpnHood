using VpnHood.Client.Device;

namespace VpnHood.Client.App.Abstractions;

public interface IAppAuthenticationService : IDisposable
{
    bool IsSignInWithGoogleSupported { get; }
    string? UserId { get; }
    HttpClient HttpClient { get; }
    Task SignInWithGoogle(IUiContext uiContext);
    Task SignOut(IUiContext uiContext);
}