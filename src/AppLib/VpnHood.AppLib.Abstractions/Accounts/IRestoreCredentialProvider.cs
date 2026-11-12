using VpnHood.Core.Client.Devices.Abstractions.UiContexts;

namespace VpnHood.AppLib.Abstractions.Accounts;

/// <summary>
/// The device side of zero-tap sign-in restoration (Android "Restore Credentials", a Play
/// requirement for sign-in apps from April 2027): a platform-held key pair that follows the person
/// through device-to-device transfer and cloud restore. The portal is the relying party and OWNS
/// every options document — implementations pass the JSON through to the platform credential API
/// verbatim and hand its response back verbatim, composing nothing themselves.
/// <para>
/// Each call takes the window, null where none is open; a platform API that needs one fails then.
/// </para>
/// </summary>
public interface IRestoreCredentialProvider : IDisposable
{
    /// <summary>
    /// Create (or silently replace) this device's restore credential for the portal's WebAuthn
    /// creation options. Returns the platform's registration response JSON, to be posted back to
    /// the portal verbatim.
    /// </summary>
    /// <param name="uiContext">The window, or <see langword="null"/> where none is open.</param>
    /// <param name="requestJson">The portal's WebAuthn creation options, as JSON.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the platform's registration response, as JSON.</returns>
    /// <exception cref="VpnHood.Core.Common.Exceptions.UiContextNotAvailableException">
    /// <paramref name="uiContext"/> is <see langword="null"/>, and the platform needs a window.
    /// </exception>
    /// <exception cref="InvalidOperationException">The platform answered with a response of another type.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<string> Create(IUiContext? uiContext, string requestJson, CancellationToken cancellationToken);

    /// <summary>
    /// Sign the portal's WebAuthn request options with the restored credential — the zero-tap
    /// moment on a new device. Returns the assertion response JSON verbatim, or null when this
    /// device holds no restore credential (a fresh install that was never restored).
    /// </summary>
    /// <param name="uiContext">The window, or <see langword="null"/> where none is open.</param>
    /// <param name="requestJson">The portal's WebAuthn request options, as JSON.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task whose result is the assertion response, as JSON, or <see langword="null"/> where the device holds no
    /// restore credential.
    /// </returns>
    /// <exception cref="VpnHood.Core.Common.Exceptions.UiContextNotAvailableException">
    /// <paramref name="uiContext"/> is <see langword="null"/>, and the platform needs a window.
    /// </exception>
    /// <exception cref="InvalidOperationException">The platform answered with a credential of another type.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<string?> TryGet(IUiContext? uiContext, string requestJson, CancellationToken cancellationToken);

    /// <summary>Clear this device's restore credential (sign-out). Idempotent.</summary>
    /// <param name="uiContext">The window, or <see langword="null"/> where none is open.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="VpnHood.Core.Common.Exceptions.UiContextNotAvailableException">
    /// <paramref name="uiContext"/> is <see langword="null"/>, and the platform needs a window.
    /// </exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// The platform failed to clear it, as Google's credential manager reports a failure or a cancel.
    /// </exception>
    Task Clear(IUiContext? uiContext, CancellationToken cancellationToken);
}
