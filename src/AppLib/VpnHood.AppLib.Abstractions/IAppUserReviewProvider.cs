using VpnHood.Core.Client.Devices.Abstractions.UiContexts;

namespace VpnHood.AppLib.Abstractions;

/// <summary>Represents the store's own way to ask the person for a review of the app.</summary>
public interface IAppUserReviewProvider
{
    /// <summary>Asks the person for a review, as the store shows it; the store may show nothing.</summary>
    /// <param name="uiContext">The app's UI context.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task that completes once the store's flow has ended, or, as the App Store's, once the request is handed to the
    /// store, which shows its dialog on its own terms.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The store could not show its dialog, as the App Store's where no scene of the app is in front.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The store needs a desktop window, which the UI context is not, as the Microsoft Store's.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task RequestReview(IUiContext uiContext, CancellationToken cancellationToken);
}