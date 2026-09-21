using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.ApiClients;

namespace VpnHood.AppUi.Common;

// The web UI's ErrorHandler: the sentence for a failure, and the buttons that go with it. The app
// reports a failure as an ApiError - a type name and a bag of data - whether it happened behind an
// HTTP call there or a method call here, so the two read the same fields. What the sentence depends
// on besides the failure comes in as the ErrorContext, read by the UI that holds the app's state.
// Every button is decided here, so a dialog only draws what it is given.
public static class ErrorMessages
{
    public static ErrorMessage For(Exception exception, ErrorContext context)
    {
        return For(exception.ToApiError(), context);
    }

    public static ErrorMessage For(ApiError error, ErrorContext context)
    {
        return WithReport(Map(error, context), context);
    }

    // a sentence of the UI's own, shown the way a failure is
    public static ErrorMessage ForText(string text, ErrorContext context)
    {
        return WithReport(new ErrorMessage.Dialog(text), context);
    }

    // The report is offered on every dialog while the app asks for one - and only on a dialog,
    // which is the one outcome that has buttons at all.
    private static ErrorMessage WithReport(ErrorMessage message, ErrorContext context)
    {
        if (!context.PromptForLog || message is not ErrorMessage.Dialog dialog)
            return message;

        if (dialog.Actions.Contains(ErrorAction.OpenReport))
            return dialog;

        return dialog with { Actions = dialog.Actions.Append(ErrorAction.OpenReport).ToArray() };
    }

    private static ErrorMessage Map(ApiError error, ErrorContext context)
    {
        var strings = Strings.Current;
        var data = error.Data;

        // a cancellation is the person's own doing, whichever class carried it
        if (error.TypeName is nameof(OperationCanceledException) or nameof(TaskCanceledException))
            return new ErrorMessage.Ignored();

        switch (error.GetExceptionType()) {
            case ExceptionType.UserCanceled:
                return new ErrorMessage.Ignored();

            case ExceptionType.UnreachableServer:
                return new ErrorMessage.Dialog(strings.UnreachableServerMessage, Diagnose(context));

            case ExceptionType.UnreachableServerLocation: {
                // after a diagnosis the sentence is all there is: no retry, trial or diagnosis helps
                if (context.HasDiagnoseRequested)
                    return new ErrorMessage.Dialog(strings.UnreachableServerLocationMessage);

                // the retry and the trial connect on the profile, so they need one
                var isAutoLocation = data.TryGetValue("IsAutoLocation", out var auto) && ToBoolean(auto);
                if (!isAutoLocation && context.HasClientProfile)
                    return new ErrorMessage.Dialog(strings.UnreachableServerLocationMessageWithChangeToAuto,
                        ErrorAction.ChangeServerToAuto);

                if (context is { HasClientProfile: true, IsPremiumUser: false, CanTryPremium: true })
                    return new ErrorMessage.Dialog(strings.UnreachableServerLocationMessageWithTryPremium,
                        ErrorAction.TryPremium);

                return new ErrorMessage.Dialog(strings.UnreachableServerLocationMessage, ErrorAction.Diagnose);
            }

            case ExceptionType.RequestQuickLaunch:
                return new ErrorMessage.Dialog(strings.QuickLaunchTurnOnError);
            case ExceptionType.NoInternet:
                return new ErrorMessage.Dialog(strings.NoInternetMsg, Diagnose(context));
            case ExceptionType.ShowAdNoUi:
                return new ErrorMessage.Dialog(strings.ShowAdNoUiMsg);
            case ExceptionType.VpnServiceUnreachable:
                return new ErrorMessage.Dialog(strings.VpnServiceUnreachableMsg);
            case ExceptionType.VpnServiceTimeout:
                return new ErrorMessage.Dialog(strings.VpnServiceTimeoutMsg);
            case ExceptionType.VpnServiceNotReady:
                return new ErrorMessage.Dialog(strings.VpnServiceNotReadyMsg);
            case ExceptionType.NoStableVpn:
                return new ErrorMessage.Dialog(strings.NoStableVpnMsg);
            case ExceptionType.RewardNotEarned:
                return new ErrorMessage.Dialog(strings.RewardNotEarnedMsg);
            case ExceptionType.NoErrorFound:
                return new ErrorMessage.Dialog(strings.DiagnoseFinishedNoErrorMsg);
            case ExceptionType.Maintenance:
                return new ErrorMessage.Dialog(strings.MaintenanceModeMsg);
            case ExceptionType.VpnServiceRevoked:
                return new ErrorMessage.Dialog(strings.VpnServiceRevokedMsg);
            case ExceptionType.PremiumOnly:
                return PremiumOnly(data, error.Message);
            case ExceptionType.LoadAd:
                return new ErrorMessage.Dialog(strings.RewardedAdLoadErrorMsg);
            case ExceptionType.ShowAd:
                return new ErrorMessage.Dialog(strings.RewardedAdShowErrorMsg);
            case ExceptionType.AdBlocker:
                return AdBlocker(data, error.Message, context);
            case ExceptionType.ConnectionTimeout:
                return new ErrorMessage.Dialog(strings.ConnectionTimeoutMsg, Diagnose(context));
            case ExceptionType.Session:
                return Session(data, error.Message, context);
            case ExceptionType.UnreachableProxyServer:
                return new ErrorMessage.Dialog(strings.UnreachableProxiesMessage);
            case ExceptionType.Billing:
                return Billing(data);
            default:
                return new ErrorMessage.Dialog(error.Message);
        }
    }

    // a diagnosis is offered once: not on what the diagnosis itself reports
    private static IReadOnlyList<ErrorAction> Diagnose(ErrorContext context)
    {
        return context.HasDiagnoseRequested ? [] : [ErrorAction.Diagnose];
    }

    // an ad blocked by a private DNS has a page of its own where custom DNS is sold as premium; any
    // other blocker, or a build that sells no custom DNS, gets the sentence
    private static ErrorMessage AdBlocker(IReadOnlyDictionary<string, string?> data, string message, ErrorContext context)
    {
        var isPrivateDns = data.TryGetValue("IsPrivateDns", out var privateDns) && ToBoolean(privateDns);
        return isPrivateDns && context.IsCustomDnsPremiumFeature
            ? new ErrorMessage.Page(ErrorPage.PrivateDns)
            : new ErrorMessage.Dialog(message);
    }

    private static ErrorMessage Session(IReadOnlyDictionary<string, string?> data, string message, ErrorContext context)
    {
        var strings = Strings.Current;
        if (!data.TryGetValue("ErrorCode", out var codeText) || !Enum.TryParse<SessionErrorCode>(codeText, true, out var code))
            return new ErrorMessage.Dialog(message);

        switch (code) {
            case SessionErrorCode.SessionSuppressedBy:
                return new ErrorMessage.Dialog(strings.SessionSuppressedByOther);

            case SessionErrorCode.AccessExpired:
                if (!context.IsPremiumSupported)
                    return new ErrorMessage.Dialog(strings.ServerKeyExpired);
                if (context.IsPremiumByAccount)
                    return new ErrorMessage.Dialog(strings.SubscriptionNotProvisionedMsg);
                return new ErrorMessage.Dialog(strings.PremiumAccessExpiredMsg, CodeActions(context));

            case SessionErrorCode.SessionExpired:
                return new ErrorMessage.Dialog(strings.PremiumConnectionExpiredMsg);
            case SessionErrorCode.DailyLimitExceeded:
                return new ErrorMessage.Dialog(strings.DailyLimitExceededMsg);

            case SessionErrorCode.AccessCodeRejected:
                if (context.IsPremiumByAccount)
                    return new ErrorMessage.Dialog(strings.SubscriptionNotProvisionedMsg);
                return new ErrorMessage.Dialog(strings.InvalidAccessCode, CodeActions(context));

            case SessionErrorCode.PlanRejected:
                return new ErrorMessage.Dialog(strings.PlanRejectedMsg);
            case SessionErrorCode.Maintenance:
                return new ErrorMessage.Dialog(strings.MaintenanceModeMsg);
            case SessionErrorCode.NoServerAvailable:
                return new ErrorMessage.Dialog(strings.NoServerAvailableMsg);
            case SessionErrorCode.PremiumLocation:
                return new ErrorMessage.Dialog(strings.PremiumLocationMsg);
            case SessionErrorCode.RewardedAdRejected:
                return new ErrorMessage.Dialog(strings.RewardNotEarnedMsg);
            default:
                return new ErrorMessage.Dialog(message);
        }
    }

    // a refused code is kept; what is offered is what exists (keyring plan §8): Restore Premium,
    // plus Change code wherever this build takes a typed one
    private static IReadOnlyList<ErrorAction> CodeActions(ErrorContext context)
    {
        if (!context.HasAccessCode)
            return [];

        return context.CanImportAccessCode
            ? [ErrorAction.RestorePremium, ErrorAction.ChangeAccessCode]
            : [ErrorAction.RestorePremium];
    }

    private static ErrorMessage PremiumOnly(IReadOnlyDictionary<string, string?> data, string message)
    {
        var strings = Strings.Current;
        // the feature by its name in AppFeature, as the app writes it into the error
        data.TryGetValue("Feature", out var featureText);
        var feature = Enum.TryParse<AppFeature>(featureText, true, out var parsed) ? parsed : (AppFeature?)null;
        return feature switch {
            AppFeature.QuickLaunch => new ErrorMessage.Dialog(strings.QuickLaunchNotSupportedMsg),
            AppFeature.AlwaysOn => new ErrorMessage.Dialog(strings.AlwaysOnNotSupportedMsg),
            _ => new ErrorMessage.Dialog(message)
        };
    }

    // store-agnostic: which store failed is the billing provider's business
    private static ErrorMessage Billing(IReadOnlyDictionary<string, string?> data)
    {
        var strings = Strings.Current;
        data.TryGetValue("BillingErrorCode", out var code);
        data.TryGetValue("StoreMessage", out var storeMessage);
        return code switch {
            "Cancelled" => new ErrorMessage.Ignored(),
            "Pending" => new ErrorMessage.Dialog(strings.BillingPendingPurchase),
            "Unavailable" => new ErrorMessage.Dialog(strings.BillingUnavailable),
            "NetworkError" => new ErrorMessage.Dialog(strings.BillingNetworkError),
            "ProductUnavailable" => new ErrorMessage.Dialog(strings.BillingItemUnavailable),
            "AlreadyOwned" => new ErrorMessage.Dialog(strings.SelectedPlanAlreadySubscribed),
            "NotOwned" => new ErrorMessage.Dialog(strings.BillingItemNotOwned),
            _ => new ErrorMessage.Dialog(strings.OrderProcessingFailed +
                                  (string.IsNullOrEmpty(storeMessage) ? "" : $" {strings.StoreExceptionMessage} {storeMessage}"))
        };
    }

    private static bool ToBoolean(string? value)
    {
        return value?.Trim().ToLowerInvariant() is "true" or "1" or "yes" or "on";
    }
}
