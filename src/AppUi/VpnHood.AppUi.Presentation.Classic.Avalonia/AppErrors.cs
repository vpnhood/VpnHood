using VpnHood.AppLib.Api.App;
using VpnHood.AppUi.Common;
using VpnHood.AppUi.Hosting.Avalonia;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia;

// What a failure's message and buttons depend on besides the failure (ErrorMessages.For): the
// standing of the session and the profile, and what the app asks for, off the state as it is when
// the failure is shown.
public static class AppErrors
{
    public static ErrorContext Context => new() {
        HasDiagnoseRequested = VhApp.State.HasDiagnoseRequested,
        HasClientProfile = VhApp.ClientProfileId != null,
        IsPremiumSupported = VhApp.IsPremiumSupported,
        IsPremiumUser = VhApp.IsPremiumUser,
        IsPremiumByAccount = VhApp.IsPremiumByAccount,
        CanTryPremium = VhApp.CanTryPremium,
        HasAccessCode = VhApp.State.ClientProfile?.HasAccessCode == true,
        CanImportAccessCode = VhApp.CanImportAccessCode,
        IsCustomDnsPremiumFeature = VhApp.IsPremiumFeature(AppFeature.CustomDns),
        PromptForLog = VhApp.State.PromptForLog
    };
}
