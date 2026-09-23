using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppLib.Abstractions.Accounts;

public interface IAuthenticationExternalProvider : IDisposable
{
    /// <summary>
    /// The identity provider this implementation drives, self-declared as a free-form string id
    /// (lowercase by convention; see AppAuthProviders for the well-known ones) so nothing downstream
    /// ever hardcodes it: the portal sends it verbatim as the wire discriminator, AppFeatures reports
    /// it, and the UI picks the matching "Sign in with X" label. A string rather than an enum so a
    /// third-party app on these packages can add its own provider — no library change, no consumer
    /// changes.
    /// </summary>
    public string ProviderId { get; }

    public Task<string> SignIn(IUiContext uiContext, bool isSilentLogin, CancellationToken cancellationToken);
    public Task SignOut(IUiContext uiContext, CancellationToken cancellationToken);
}