using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppLib.Abstractions.Accounts;

/// <summary>
/// Who is signed in, and the credential that proves it. Deliberately not a transport: it hands out
/// a token and lets the caller own its own HttpClient, so nothing downstream inherits a client's
/// base address, TLS policy or lifetime from here.
/// <para>
/// Disposable because it owns the external providers it was given, not because it owns any client.
/// </para>
/// </summary>
public interface IAuthenticationProvider : IDisposable
{
    IReadOnlyList<string> ProviderIds { get; }

    /// <summary>
    /// The human account website behind this provider (where a person manages the account and
    /// recovers a password), or null when there is none. The UI renders it as the escape hatch of
    /// the password sign-in form — never as an API endpoint.
    /// </summary>
    Uri? AccountWebsiteUrl { get; }

    /// <summary>
    /// The signed-in account's id, or null. Ambient and cheap: it is read straight from the
    /// persisted session, so a device signed in yesterday knows it without any call.
    /// </summary>
    string? UserId { get; }

    /// <summary>
    /// The credential for the current session, renewed silently when it is near expiry — hence
    /// async, and hence not a property beside <see cref="UserId" />. Null when nobody is signed in;
    /// an anonymous call simply never asks for one.
    /// </summary>
    Task<string?> GetAccessToken(CancellationToken cancellationToken);

    /// <summary>
    /// The backend refused this token: the session is over. The token is passed back so a stale
    /// in-flight refusal cannot end a session that has meanwhile been replaced by a newer sign-in.
    /// </summary>
    void InvalidateAccessToken(string accessToken);

    Task<SignInResult> SignIn(IUiContext uiContext, SignInOptions signInOptions, CancellationToken cancellationToken);
    Task SignOut(IUiContext uiContext, CancellationToken cancellationToken);
}
