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
        return WithReport(new ErrorMessage(text), context);
    }

    // the report is offered on every dialog while the app asks for one
    private static ErrorMessage WithReport(ErrorMessage message, ErrorContext context)
    {
        if (message.IsIgnored || message.Page != ErrorPage.None || !context.PromptForLog)
            return message;

        return message with { Actions = message.Actions.Append(ErrorAction.OpenReport).ToArray() };
    }

    private static ErrorMessage Map(ApiError error, ErrorContext context)
    {
        var strings = Strings.Current;
        var data = error.Data;

        // a cancellation is the person's own doing, whichever class carried it
        if (error.TypeName is nameof(OperationCanceledException) or nameof(TaskCanceledException))
            return ErrorMessage.Ignored;

        switch (error.GetExceptionType()) {
            case ExceptionType.UserCanceled:
                return ErrorMessage.Ignored;

            case ExceptionType.UnreachableServer:
                return new ErrorMessage(strings.UnreachableServerMessage, Diagnose(context));

            case ExceptionType.UnreachableServerLocation: {
                // after a diagnosis the sentence is all there is: no retry, trial or diagnosis helps
                if (context.HasDiagnoseRequested)
                    return new ErrorMessage(strings.UnreachableServerLocationMessage);

                // the retry and the trial connect on the profile, so they need one
                var isAutoLocation = data.TryGetValue("IsAutoLocation", out var auto) && ToBoolean(auto);
                if (!isAutoLocation && context.HasClientProfile)
                    return new ErrorMessage(strings.UnreachableServerLocationMessageWithChangeToAuto,
                        ErrorAction.ChangeServerToAuto);

                if (context is { HasClientProfile: true, IsPremiumUser: false, CanTryPremium: true })
                    return new ErrorMessage(strings.UnreachableServerLocationMessageWithTryPremium,
                        ErrorAction.TryPremium);

                return new ErrorMessage(strings.UnreachableServerLocationMessage, ErrorAction.Diagnose);
            }

            case ExceptionType.RequestQuickLaunch:
                return new ErrorMessage(strings.QuickLaunchTurnOnError);
            case ExceptionType.NoInternet:
                return new ErrorMessage(strings.NoInternetMsg, Diagnose(context));
            case ExceptionType.ShowAdNoUi:
                return new ErrorMessage(strings.ShowAdNoUiMsg);
            case ExceptionType.VpnServiceUnreachable:
                return new ErrorMessage(strings.VpnServiceUnreachableMsg);
            case ExceptionType.VpnServiceTimeout:
                return new ErrorMessage(strings.VpnServiceTimeoutMsg);
            case ExceptionType.VpnServiceNotReady:
                return new ErrorMessage(strings.VpnServiceNotReadyMsg);
            case ExceptionType.NoStableVpn:
                return new ErrorMessage(strings.NoStableVpnMsg);
            case ExceptionType.RewardNotEarned:
                return new ErrorMessage(strings.RewardNotEarnedMsg);
            case ExceptionType.NoErrorFound:
                return new ErrorMessage(strings.DiagnoseFinishedNoErrorMsg);
            case ExceptionType.Maintenance:
                return new ErrorMessage(strings.MaintenanceModeMsg);
            case ExceptionType.VpnServiceRevoked:
                return new ErrorMessage(strings.VpnServiceRevokedMsg);
            case ExceptionType.PremiumOnly:
                return PremiumOnly(data, error.Message);
            case ExceptionType.LoadAd:
                return new ErrorMessage(strings.RewardedAdLoadErrorMsg);
            case ExceptionType.ShowAd:
                return new ErrorMessage(strings.RewardedAdShowErrorMsg);
            case ExceptionType.AdBlocker:
                return AdBlocker(data, error.Message, context);
            case ExceptionType.ConnectionTimeout:
                return new ErrorMessage(strings.ConnectionTimeoutMsg, Diagnose(context));
            case ExceptionType.Session:
                return Session(data, error.Message, context);
            case ExceptionType.UnreachableProxyServer:
                return new ErrorMessage(strings.UnreachableProxiesMessage);
            case ExceptionType.Billing:
                return Billing(data);
            default:
                return new ErrorMessage(error.Message);
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
            ? ErrorMessage.OnPage(ErrorPage.PrivateDns)
            : new ErrorMessage(message);
    }

    private static ErrorMessage Session(IReadOnlyDictionary<string, string?> data, string message, ErrorContext context)
    {
        var strings = Strings.Current;
        if (!data.TryGetValue("ErrorCode", out var codeText) || !Enum.TryParse<SessionErrorCode>(codeText, true, out var code))
            return new ErrorMessage(message);

        switch (code) {
            case SessionErrorCode.SessionSuppressedBy:
                return new ErrorMessage(strings.SessionSuppressedByOther);

            case SessionErrorCode.AccessExpired:
                if (!context.IsPremiumSupported)
                    return new ErrorMessage(strings.ServerKeyExpired);
                if (context.IsPremiumByAccount)
                    return new ErrorMessage(strings.SubscriptionNotProvisionedMsg);
                return new ErrorMessage(strings.PremiumAccessExpiredMsg, CodeActions(context));

            case SessionErrorCode.SessionExpired:
                return new ErrorMessage(strings.PremiumConnectionExpiredMsg);
            case SessionErrorCode.DailyLimitExceeded:
                return new ErrorMessage(strings.DailyLimitExceededMsg);

            case SessionErrorCode.AccessCodeRejected:
                if (context.IsPremiumByAccount)
                    return new ErrorMessage(strings.SubscriptionNotProvisionedMsg);
                return new ErrorMessage(strings.InvalidAccessCode, CodeActions(context));

            case SessionErrorCode.PlanRejected:
                return new ErrorMessage(strings.PlanRejectedMsg);
            case SessionErrorCode.Maintenance:
                return new ErrorMessage(strings.MaintenanceModeMsg);
            case SessionErrorCode.NoServerAvailable:
                return new ErrorMessage(strings.NoServerAvailableMsg);
            case SessionErrorCode.PremiumLocation:
                return new ErrorMessage(strings.PremiumLocationMsg);
            case SessionErrorCode.RewardedAdRejected:
                return new ErrorMessage(strings.RewardNotEarnedMsg);
            default:
                return new ErrorMessage(message);
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
            AppFeature.QuickLaunch => new ErrorMessage(strings.QuickLaunchNotSupportedMsg),
            AppFeature.AlwaysOn => new ErrorMessage(strings.AlwaysOnNotSupportedMsg),
            _ => new ErrorMessage(message)
        };
    }

    // store-agnostic: which store failed is the billing provider's business
    private static ErrorMessage Billing(IReadOnlyDictionary<string, string?> data)
    {
        var strings = Strings.Current;
        data.TryGetValue("BillingErrorCode", out var code);
        data.TryGetValue("StoreMessage", out var storeMessage);
        return code switch {
            "Cancelled" => ErrorMessage.Ignored,
            "Pending" => new ErrorMessage(strings.BillingPendingPurchase),
            "Unavailable" => new ErrorMessage(strings.BillingUnavailable),
            "NetworkError" => new ErrorMessage(strings.BillingNetworkError),
            "ProductUnavailable" => new ErrorMessage(strings.BillingItemUnavailable),
            "AlreadyOwned" => new ErrorMessage(strings.SelectedPlanAlreadySubscribed),
            "NotOwned" => new ErrorMessage(strings.BillingItemNotOwned),
            _ => new ErrorMessage(strings.OrderProcessingFailed +
                                  (string.IsNullOrEmpty(storeMessage) ? "" : $" {strings.StoreExceptionMessage} {storeMessage}"))
        };
    }

    private static bool ToBoolean(string? value)
    {
        return value?.Trim().ToLowerInvariant() is "true" or "1" or "yes" or "on";
    }
}
