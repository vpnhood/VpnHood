using VpnHood.AppLib.Api.Billing;

// ReSharper disable UnusedMemberInSuper.Global

namespace VpnHood.AppLib.Api;

/// <summary>Represents the app's API for the in-app purchase: the plans, the purchase and its restore.</summary>
/// <remarks>
/// Where the app has no account backend, every call fails. A failure comes as <see cref="IAppApi"/> describes, in
/// process and over HTTP.
/// </remarks>
public interface IBillingApi
{
    /// <summary>Gets the plans the app may sell, as the store prices them.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the plans.</returns>
    /// <exception cref="InvalidOperationException">The account backend names no store for the app.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// The account backend refused the request; or, over HTTP, the app failed with an exception of a type the transport
    /// does not rebuild, or answered with an error it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    /// <remarks>A store that fails comes as the app's <c>BillingException</c>.</remarks>
    Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlans(CancellationToken cancellationToken);

    /// <summary>Runs the store's payment flow, and turns the purchase into access.</summary>
    /// <param name="purchaseParams">The plan the person chose.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the purchase is turned into access.</returns>
    /// <exception cref="System.IO.InvalidDataException">For Google Play, the plan's token decodes to JSON <c>null</c>.</exception>
    /// <exception cref="System.Text.Json.JsonException">For Google Play, the plan's token is not valid.</exception>
    /// <exception cref="InvalidOperationException">
    /// The store cannot sell now, the signed-in account changed meanwhile, or the order was not settled.
    /// </exception>
    /// <exception cref="ArgumentException">The store does not know the plan.</exception>
    /// <exception cref="NotSupportedException">The store does not take the purchase.</exception>
    /// <exception cref="System.Security.Authentication.AuthenticationException">Nobody is signed in.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.Exceptions.AlreadyExistsException">The account is premium already.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// The account backend refused the request; or, over HTTP, the app failed with an exception of a type the transport
    /// does not rebuild, or answered with an error it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    /// <remarks>
    /// The store's proof of the purchase stays with the app. A store that fails comes as the app's <c>BillingException</c>,
    /// whose code is <c>Cancelled</c> where the person closed the payment flow; a store that shows a window needs the
    /// app's, and fails without it, as the app's <c>UiContextNotAvailableException</c>.
    /// </remarks>
    Task Purchase(PurchaseParams purchaseParams, CancellationToken cancellationToken);

    /// <summary>Restores a subscription the store account owns, without a prompt.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task whose result is <see langword="true"/> where a purchase was restored, or <see langword="false"/> where none
    /// was; a subscription bought on the web comes back through the account instead.
    /// </returns>
    /// <exception cref="InvalidOperationException">The signed-in account changed meanwhile, or the order was not settled.</exception>
    /// <exception cref="System.Security.Authentication.AuthenticationException">Nobody is signed in.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// The account backend refused the request; or, over HTTP, the app failed with an exception of a type the transport
    /// does not rebuild, or answered with an error it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    /// <remarks>A store that fails comes as the app's <c>BillingException</c>.</remarks>
    Task<bool> RestorePurchase(CancellationToken cancellationToken);

    /// <summary>Opens the store's screen for managing the subscription.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="NotSupportedException">The device cannot show the store's subscription management.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    /// <remarks>
    /// The UI offers it only where the subscription's <see cref="VpnHood.AppLib.Api.Accounts.Subscription.Management"/> is
    /// <see cref="VpnHood.AppLib.Api.Accounts.SubscriptionManagement.Available"/>. It needs the app's window where the store
    /// shows one, and fails without it, as the app's <c>UiContextNotAvailableException</c>.
    /// </remarks>
    Task OpenSubscriptionManagement(CancellationToken cancellationToken);
}
