using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppLib.Abstractions.Accounts;

/// <summary>
/// The device side of zero-tap sign-in restoration (Android "Restore Credentials", a Play
/// requirement for sign-in apps from April 2027): a platform-held key pair that follows the person
/// through device-to-device transfer and cloud restore. The portal is the relying party and OWNS
/// every options document — implementations pass the JSON through to the platform credential API
/// verbatim and hand its response back verbatim, composing nothing themselves.
/// </summary>
public interface IRestoreCredentialProvider : IDisposable
{
    /// <summary>
    /// Create (or silently replace) this device's restore credential for the portal's WebAuthn
    /// creation options. Returns the platform's registration response JSON, to be posted back to
    /// the portal verbatim.
    /// </summary>
    Task<string> Create(IUiContext uiContext, string requestJson, CancellationToken cancellationToken);

    /// <summary>
    /// Sign the portal's WebAuthn request options with the restored credential — the zero-tap
    /// moment on a new device. Returns the assertion response JSON verbatim, or null when this
    /// device holds no restore credential (a fresh install that was never restored).
    /// </summary>
    Task<string?> TryGet(IUiContext uiContext, string requestJson, CancellationToken cancellationToken);

    /// <summary>Clear this device's restore credential (sign-out). Idempotent.</summary>
    Task Clear(IUiContext uiContext, CancellationToken cancellationToken);
}
