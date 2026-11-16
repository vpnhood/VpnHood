using VpnHood.AppLogic.Abstractions;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;

namespace VpnHood.AppLogic.Test.Providers;

internal class TestAppUserReviewProvider : IAppUserReviewProvider
{
    public bool IsReviewRequested { get; set; }

    public async Task RequestReview(IUiContext uiContext, CancellationToken cancellationToken)
    {
        if (!await uiContext.IsActive())
            throw new InvalidOperationException("UiContext is not active.");

        IsReviewRequested = true;
    }
}