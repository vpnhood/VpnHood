namespace VpnHood.AppLib.Abstractions.Accounts;

/// <summary>
/// Where a subscription can be managed from here — cancel, change plan, change payment method.
/// A reason rather than a yes/no, because the two ways of saying no need opposite things from the
/// UI: one forbids naming a store, the other depends on naming ours.
/// </summary>
public enum SubscriptionManagement
{
    /// <summary>
    /// Another platform's store billed it — bought on Android, now signed in on an iPhone. The UI
    /// must say so WITHOUT naming that store: naming a competing store is itself a store violation
    /// (App Review 2.3.10), which is why the subscription's own StoreId must not be rendered here.
    /// </summary>
    AnotherStore,

    /// <summary>
    /// Our own store billed it, but it cannot show the screen on this device — a television, where
    /// Google Play has no subscriptions screen and forwards to a browser that may not exist. The UI
    /// may name the store here, because it is the one this build ships to, and should point the
    /// person at a device that can.
    /// </summary>
    NotOnThisDevice,

    /// <summary>
    /// This build's store billed it and can show its own screen on this device. The UI offers the
    /// control, which calls the app rather than opening any address. Deliberately NOT the zero
    /// value: an implementation that forgets to answer must fall to the harmless case, not to the
    /// one that draws a button.
    /// </summary>
    Available
}
