using VpnHood.Core.Client.Devices.Abstractions.UiContexts;

namespace VpnHood.AppLib.Abstractions;

public interface IAppUserReviewProvider
{
    Task RequestReview(IUiContext uiContext, CancellationToken cancellationToken);
}