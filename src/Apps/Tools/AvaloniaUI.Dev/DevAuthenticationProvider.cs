using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;

namespace VpnHood.App.AvaloniaUI.Dev;

// A store build's sign-in without the store or the portal: the store's own provider and a password,
// as the portal offers them, and any of them signs a sample person in at once - but an email with
// "2fa" in it, whose account has an authenticator: the password then asks for the second factor, as
// the portal does. Its code 000000 is wrong, a code longer than six characters is the backup code
// (it signs in and hands over a new one), and any other code signs in.
internal sealed class DevAuthenticationProvider(string storeAuthProviderId) : IAuthenticationProvider
{
    private bool _isChallengePending;

    public IReadOnlyList<string> ProviderIds { get; } = [storeAuthProviderId, AuthProviders.Password];
    public string? UserId { get; private set; }

    public Task<string?> GetAccessToken(CancellationToken cancellationToken)
    {
        return Task.FromResult(UserId == null ? null : "dev-access-token");
    }

    public Task<Uri?> GetPasswordResetUrl(CancellationToken cancellationToken)
    {
        return Task.FromResult<Uri?>(new Uri("https://account.example.com/password/reset"));
    }

    public void InvalidateAccessToken(string accessToken)
    {
        UserId = null;
    }

    public Task<SignInResult> SignIn(IUiContext? uiContext, SignInOptions signInOptions, CancellationToken cancellationToken)
    {
        if (signInOptions.TwoFactorCode is { } code)
            return Task.FromResult(CompleteChallenge(code));

        _isChallengePending = signInOptions.ProviderId == AuthProviders.Password &&
                              signInOptions.UserName?.Contains("2fa", StringComparison.OrdinalIgnoreCase) == true;
        if (_isChallengePending)
            return Task.FromResult(new SignInResult { State = SignInState.TotpRequired });

        UserId = "dev-user";
        return Task.FromResult(new SignInResult { State = SignInState.SignedIn });
    }

    // the portal's answers to the challenge form, refusals with its machine codes (Data["Code"])
    private SignInResult CompleteChallenge(string code)
    {
        if (!_isChallengePending)
            throw WithCode(new UnauthorizedAccessException("The sign-in challenge is spent or expired."), "invalid_challenge");
        if (code == "000000")
            throw WithCode(new UnauthorizedAccessException("The code is wrong."), "invalid_code");

        _isChallengePending = false;
        UserId = "dev-user";
        return new SignInResult {
            State = SignInState.SignedIn,
            NewBackupCode = code.Length > 6 ? "6ef4 7a39 d03b 2c41" : null
        };
    }

    private static UnauthorizedAccessException WithCode(UnauthorizedAccessException exception, string code)
    {
        exception.Data["Code"] = code;
        return exception;
    }

    public Task SignOut(IUiContext? uiContext, CancellationToken cancellationToken)
    {
        UserId = null;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }
}
