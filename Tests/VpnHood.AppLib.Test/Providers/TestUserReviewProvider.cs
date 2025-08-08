using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.AppLib.Test.Providers;

internal class TestUserReviewProvider : IAppUserReviewProvider
{
    public bool IsReviewRequested { get; set; }

    public async Task RequestReview(IUiContext uiContext, CancellationToken cancellationToken)
    {
        if (!await uiContext.IsActive())
            throw new InvalidOperationException("UiContext is not active.");

        IsReviewRequested = true;
    }
}
