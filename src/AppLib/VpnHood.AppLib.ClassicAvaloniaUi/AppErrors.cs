using VpnHood.AppLib.Assets;
using VpnHood.AppLib.AvaloniaUI;

namespace VpnHood.AppLib.ClassicAvaloniaUi;

// What a failure's message depends on besides the failure (ErrorMessages.For): the standing of
// the session and the profile, off the state as it is when the failure is shown.
public static class AppErrors
{
    public static ErrorContext Context => new() {
        HasDiagnoseRequested = AppData.State.HasDiagnoseRequested,
        IsPremiumSupported = AppData.IsPremiumSupported,
        IsPremiumUser = AppData.IsPremiumUser,
        IsPremiumByAccount = AppData.IsPremiumByAccount,
        CanTryPremium = AppData.CanTryPremium,
        HasAccessCode = AppData.State.ClientProfile?.HasAccessCode == true,
        CanImportAccessCode = AppData.CanImportAccessCode
    };
}
