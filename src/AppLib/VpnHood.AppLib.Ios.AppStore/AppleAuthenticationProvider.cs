using AuthenticationServices;
using Foundation;
using UIKit;
using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppLib.Ios.AppStore;

/// <summary>
/// Sign in with Apple (ASAuthorizationAppleIdProvider — fully bound in
/// Microsoft.iOS, no Swift needed). Returns the identity token JWS, which the
/// portal's POST /auth/sessions verifies against Apple's published keys.
/// </summary>
public class AppleAuthenticationProvider : IAppAuthenticationExternalProvider
{
    public string SignInMethod => AppSignInMethods.Apple;

    public async Task<string> SignIn(IUiContext uiContext, bool isSilentLogin, CancellationToken cancellationToken)
    {
        // Apple has no silent token mint; an interactive-capable session is required.
        // (The system sheet completes without input when the Apple ID session is warm,
        // so "silent" renewals still work in practice — they just must not START from
        // the background.)
        if (isSilentLogin && !await uiContext.IsActive().ConfigureAwait(false))
            throw new InvalidOperationException("Silent Apple sign-in needs the app in the foreground.");

        var provider = new ASAuthorizationAppleIdProvider();
        var request = provider.CreateRequest();
        request.RequestedScopes = [ASAuthorizationScope.Email];

        var handler = new AuthorizationHandler();
        var controller = new ASAuthorizationController([request]) {
            Delegate = handler,
            PresentationContextProvider = handler
        };
        controller.PerformRequests();

        var credential = await handler.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        var identityToken = credential.IdentityToken?.ToString(NSStringEncoding.UTF8)
            ?? throw new InvalidOperationException("Apple returned no identity token.");
        return identityToken;
    }

    public Task SignOut(IUiContext uiContext, CancellationToken cancellationToken)
    {
        // Apple has no sign-out API; the portal session revoke (DELETE /auth/sessions/current) is the
        // real sign-out. Nothing to do on the device.
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }

    private sealed class AuthorizationHandler : ASAuthorizationControllerDelegate,
        IASAuthorizationControllerPresentationContextProviding
    {
        public TaskCompletionSource<ASAuthorizationAppleIdCredential> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void DidComplete(ASAuthorizationController controller, ASAuthorization authorization)
        {
            if (authorization.GetCredential<ASAuthorizationAppleIdCredential>() is { } credential)
                Completion.TrySetResult(credential);
            else
                Completion.TrySetException(new InvalidOperationException("Apple returned no credential."));
        }

        public override void DidComplete(ASAuthorizationController controller, NSError error)
        {
            // 1001 = ASAuthorizationError.Canceled — the user dismissed the sheet
            if (error.Code == (long)ASAuthorizationError.Canceled)
                Completion.TrySetException(new OperationCanceledException("Apple sign-in was cancelled."));
            else
                Completion.TrySetException(new InvalidOperationException(
                    $"Apple sign-in failed: {error.LocalizedDescription}"));
        }

        public UIWindow GetPresentationAnchor(ASAuthorizationController controller)
        {
            return UIApplication.SharedApplication.ConnectedScenes
                       .OfType<UIWindowScene>()
                       .SelectMany(scene => scene.Windows)
                       .FirstOrDefault(window => window.IsKeyWindow)
                   ?? UIApplication.SharedApplication.Windows.FirstOrDefault(window => window.IsKeyWindow)
                   ?? throw new InvalidOperationException("No key window to present Apple sign-in on.");
        }
    }
}
