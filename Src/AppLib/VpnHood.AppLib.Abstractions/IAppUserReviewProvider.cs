using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppLib.Abstractions;

public interface IAppUserReviewProvider
{
    Task RequestReview(IUiContext uiContext, CancellationToken cancellationToken);
}