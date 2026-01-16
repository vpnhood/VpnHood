using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.AppLib.Abstractions;

public interface IAppAuthenticationProvider : IDisposable
{
    bool IsSignInWithGoogleSupported { get; }
    string? UserId { get; }
    HttpClient HttpClient { get; }
    Task SignInWithGoogle(IUiContext uiContext, CancellationToken cancellationToken);
    Task SignOut(IUiContext uiContext, CancellationToken cancellationToken);
}