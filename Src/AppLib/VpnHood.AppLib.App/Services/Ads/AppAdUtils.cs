using VpnHood.AppLib.Abstractions.AdExceptions;
using VpnHood.Core.Client.Device.UiContexts;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Services.Ads;

internal static class AppAdUtils
{
    public static async Task<bool> IsActiveUi(IUiContext uiContext, bool immediately = true)
    {
        if (await uiContext.IsActive())
            return true;

        // throw exception if the UI is not available
        if (immediately)
            return false;

        // throw exception if the UI is destroyed, there is no point in waiting for it
        if (await uiContext.IsDestroyed())
            return false;

        // wait for the UI to be active
        for (var i = 0; i < 10; i++) {
            await Task.Delay(200).Vhc();
            if (await uiContext.IsActive())
                return true;
        }

        return false;
    }

    public static async Task VerifyActiveUi(IUiContext uiContext, bool immediately = true)
    {
        if (!await IsActiveUi(uiContext, immediately))
            throw new ShowAdNoUiException();
    }
}