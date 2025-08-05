using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.AppLib.Test.Providers;

internal class TestUserReviewProvider : IAppReviewProvider
{
    public bool IsReviewRequested { get; set; }

    public async Task RequestReview(IUiContext uiContext)
    {
        if (!await uiContext.IsActive())
            throw new InvalidOperationException("UiContext is not active.");

        IsReviewRequested = true;
    }
}
