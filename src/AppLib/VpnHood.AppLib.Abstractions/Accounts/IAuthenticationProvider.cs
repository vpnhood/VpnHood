using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;

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
    /// The page of the human account website behind this provider where a person recovers a
    /// password, or null when there is none. Asked when the password sign-in form opens, which
    /// offers it as its escape hatch only when a page comes back — never as an API endpoint.
    /// </summary>
    Task<Uri?> GetPasswordResetUrl(CancellationToken cancellationToken);

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

    /// <param name="uiContext">
    /// The window, or null where none is open, as in the desktop service: the portal's password form
    /// needs none; a method that shows a sheet (Google, Apple) throws
    /// <c>UiContextNotAvailableException</c> without one.
    /// </param>
    Task<SignInResult> SignIn(IUiContext? uiContext, SignInOptions signInOptions, CancellationToken cancellationToken);

    /// <param name="uiContext">
    /// The window, or null where none is open. The session ends either way; what only a window can
    /// clear on the device is left.
    /// </param>
    Task SignOut(IUiContext? uiContext, CancellationToken cancellationToken);
}
