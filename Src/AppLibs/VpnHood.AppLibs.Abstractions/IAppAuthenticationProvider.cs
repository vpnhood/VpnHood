using VpnHood.Core.Client.Device;

namespace VpnHood.AppLibs.Abstractions;

public interface IAppAuthenticationProvider : IDisposable
{
    bool IsSignInWithGoogleSupported { get; }
    string? UserId { get; }
    HttpClient HttpClient { get; }
    Task SignInWithGoogle(IUiContext uiContext);
    Task SignOut(IUiContext uiContext);
}