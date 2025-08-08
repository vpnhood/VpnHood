using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.AppLib.Abstractions;

public interface IAppUserReviewProvider
{
    Task RequestReview(IUiContext uiContext, CancellationToken cancellationToken);
}