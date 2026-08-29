using System.Security.Authentication;
using AuthenticationServices;
using Foundation;
using UIKit;
using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Client.Devices.Ios.Extensions;
using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppLib.Ios.AppStore;

/// <summary>
/// Sign in with Apple (ASAuthorizationAppleIdProvider — fully bound in
/// Microsoft.iOS, no Swift needed). Returns the identity token JWS, which the
/// portal's POST /auth/sessions verifies against Apple's published keys.
/// </summary>
public class AppleAuthenticationProvider : IAuthenticationExternalProvider
{
    public string ProviderId => AuthProviders.Apple;

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
            ?? throw new AuthenticationException("Apple returned no identity token.");
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
                Completion.TrySetException(new AuthenticationException("Apple returned no credential."));
        }

        public override void DidComplete(ASAuthorizationController controller, NSError error)
        {
            // 1001 = ASAuthorizationError.Canceled. Apple reports Canceled BOTH when the user dismisses
            // the sheet and when its own server-side check fails ("Sign Up Not Completed") and the user
            // dismisses that alert, so the flattened detail must survive — as the inner exception, which
            // reaches the log/report, never the user-facing message. UserCanceledException is what the
            // UI silences and AuthenticationException is what it translates (both same as the Google
            // provider); the raw NSError chain would otherwise surface verbatim in the error dialog.
            if (error.Code == (long)ASAuthorizationError.Canceled)
                Completion.TrySetException(new UserCanceledException("Apple sign-in was cancelled.",
                    new InvalidOperationException(error.Describe())));
            else
                Completion.TrySetException(new AuthenticationException("Apple sign-in failed.",
                    new InvalidOperationException(error.Describe())));
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
