namespace VpnHood.AppLib.Abstractions.Billing;

/// <summary>
/// Well-known store ids — the billing counterpart of <see cref="Accounts.AuthProviders" />, and the
/// same kind of contract: a free-form lowercase STRING self-declared by the app's
/// <see cref="IBillingProvider" />, deliberately not an enum, so a third-party store can introduce
/// its own id without a change to this library.
/// <para>
/// The id is consumed verbatim by the account backend: it selects which app row a purchase belongs
/// to, which store API re-verifies the proof, and which subscriptions this device may manage.
/// Changing one is a breaking change on the wire.
/// </para>
/// </summary>
public static class StoreIds
{
    /// <summary>Google Play: <c>googleplay</c>.</summary>
    public const string GooglePlay = "googleplay";

    /// <summary>Apple's App Store: <c>appstore</c>.</summary>
    public const string AppStore = "appstore";

    /// <summary>The Microsoft Store: <c>microsoft</c>.</summary>
    public const string Microsoft = "microsoft";

    /// <summary>
    /// The direct-download distribution channel, which sells on the website: the web billing provider
    /// carries this id. It names the backend's app registration for such builds, and the backend
    /// serves priced web plans (checkout on the website) only to apps registered under it.
    /// </summary>
    public const string Web = "web";
}
