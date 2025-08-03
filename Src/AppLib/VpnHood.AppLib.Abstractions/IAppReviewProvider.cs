using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.AppLib.Abstractions;

public interface IAppReviewProvider
{
    Task RequestReview(IUiContext uiContext);
}