using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppLib.Abstractions;

public interface IAppAuthenticationProvider : IDisposable
{
    IReadOnlyList<string> SignInMethods { get; }
    string? UserId { get; }
    HttpClient HttpClient { get; }
    Task SignIn(IUiContext uiContext, AppSignInOptions signInOptions, CancellationToken cancellationToken);
    Task SignOut(IUiContext uiContext, CancellationToken cancellationToken);
}
