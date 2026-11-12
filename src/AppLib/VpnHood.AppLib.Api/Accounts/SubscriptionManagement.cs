namespace VpnHood.AppLib.Api.Accounts;

/// <summary>
/// Specifies where a subscription can be managed from this device, to cancel it or change its plan or payment, or why
/// not.
/// </summary>
/// <remarks>The two reasons ask opposite things of the UI: one may not name a store, the other names this build's.</remarks>
public enum SubscriptionManagement
{
    /// <summary>
    /// Another platform's store billed it, as Google Play for a person now on an iPhone. The UI says so without naming that
    /// store, as a store's rules forbid naming a competing one, and does not show <see cref="Subscription.StoreId"/>.
    /// </summary>
    AnotherStore,

    /// <summary>
    /// This build's store billed it, but cannot show its screen on this device, as on a TV, where Google Play forwards to a
    /// browser the TV may not have. The UI may name the store, and points the person to a device that can.
    /// </summary>
    NotOnThisDevice,

    /// <summary>
    /// This build's store billed it and can show its screen here: the UI offers the control, which calls the app. It is not
    /// the default value, so a provider that sets nothing falls to a case that draws no button.
    /// </summary>
    Available
}
