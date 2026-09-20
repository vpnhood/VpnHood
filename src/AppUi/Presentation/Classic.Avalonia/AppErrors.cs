using VpnHood.AppUi.Services;
using VpnHood.AppUi.Hosting.Avalonia;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia;

// What a failure's message depends on besides the failure (ErrorMessages.For): the standing of
// the session and the profile, off the state as it is when the failure is shown.
public static class AppErrors
{
    public static ErrorContext Context => new() {
        HasDiagnoseRequested = AppModel.State.HasDiagnoseRequested,
        IsPremiumSupported = AppModel.IsPremiumSupported,
        IsPremiumUser = AppModel.IsPremiumUser,
        IsPremiumByAccount = AppModel.IsPremiumByAccount,
        CanTryPremium = AppModel.CanTryPremium,
        HasAccessCode = AppModel.State.ClientProfile?.HasAccessCode == true,
        CanImportAccessCode = AppModel.CanImportAccessCode
    };
}
