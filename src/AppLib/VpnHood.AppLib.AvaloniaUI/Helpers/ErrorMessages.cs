using VpnHood.AppLib.AvaloniaUI.Resources;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.ApiClients;

namespace VpnHood.AppLib.AvaloniaUI.Helpers;

// The web UI's ErrorHandler: the sentence for a failure, and the buttons that go with it. The app
// reports a failure as an ApiError - a type name and a bag of data - whether it happened behind an
// HTTP call there or a method call here, so the two read the same fields.
internal static class ErrorMessages
{
    public static ErrorMessage For(Exception exception)
    {
        return For(exception.ToApiError());
    }

    public static ErrorMessage For(ApiError error)
    {
        var strings = Strings.Current;
        var data = error.Data;

        switch (error.TypeName) {
            // silenced on purpose
            case "UserCanceledException":
            case "OperationCanceledException":
            case "TaskCanceledException":
                return ErrorMessage.Ignored;

            case "UnreachableServerException":
                return new ErrorMessage(strings.UnreachableServerMessage, new ErrorActions { ShowDiagnose = true });

            case "UnreachableServerLocationException": {
                if (AppData.State.HasDiagnoseRequested)
                    return new ErrorMessage(strings.UnreachableServerLocationMessage);

                var isAutoLocation = data.TryGetValue("IsAutoLocation", out var auto) && ToBoolean(auto);
                if (!isAutoLocation)
                    return new ErrorMessage(strings.UnreachableServerLocationMessageWithChangeToAuto,
                        new ErrorActions { ShowChangeServerToAuto = true });

                if (!AppData.IsPremiumUser && AppData.CanTryPremium)
                    return new ErrorMessage(strings.UnreachableServerLocationMessageWithTryPremium,
                        new ErrorActions { ShowTryPremium = true });

                return new ErrorMessage(strings.UnreachableServerLocationMessage, new ErrorActions { ShowDiagnose = true });
            }

            case "RequestQuickLaunchException":
                return new ErrorMessage(strings.QuickLaunchTurnOnError);
            case "NoInternetException":
                return new ErrorMessage(strings.NoInternetMsg, new ErrorActions { ShowDiagnose = true });
            case "ShowAdNoUiException":
                return new ErrorMessage(strings.ShowAdNoUiMsg);
            case "VpnServiceUnreachableException":
                return new ErrorMessage(strings.VpnServiceUnreachableMsg);
            case "VpnServiceTimeoutException":
                return new ErrorMessage(strings.VpnServiceTimeoutMsg);
            case "VpnServiceNotReadyException":
                return new ErrorMessage(strings.VpnServiceNotReadyMsg);
            case "NoStableVpnException":
                return new ErrorMessage(strings.NoStableVpnMsg);
            case "RewardNotEarnedException":
                return new ErrorMessage(strings.RewardNotEarnedMsg);
            case "NoErrorFoundException":
                return new ErrorMessage(strings.DiagnoseFinishedNoErrorMsg);
            case "MaintenanceException":
                return new ErrorMessage(strings.MaintenanceModeMsg);
            case "VpnServiceRevokedException":
                return new ErrorMessage(strings.VpnServiceRevokedMsg);
            case "PremiumOnlyException":
                return PremiumOnly(data);
            case "LoadAdException":
                return new ErrorMessage(strings.RewardedAdLoadErrorMsg);
            case "ShowAdException":
                return new ErrorMessage(strings.RewardedAdShowErrorMsg);
            case "AdBlockerException":
                return new ErrorMessage("", new ErrorActions { IsPrivateDnsError = true });
            case "ConnectionTimeoutException":
                return new ErrorMessage(strings.ConnectionTimeoutMsg, new ErrorActions { ShowDiagnose = true });
            case "SessionException":
                return Session(data, error.Message);
            case "UnreachableProxyServerException":
                return new ErrorMessage(strings.UnreachableProxiesMessage);
            case "BillingException":
                return Billing(data);
            default:
                return new ErrorMessage(error.Message);
        }
    }

    private static ErrorMessage Session(IReadOnlyDictionary<string, string?> data, string message)
    {
        var strings = Strings.Current;
        if (!data.TryGetValue("ErrorCode", out var codeText) || !Enum.TryParse<SessionErrorCode>(codeText, true, out var code))
            return new ErrorMessage(message);

        switch (code) {
            case SessionErrorCode.SessionSuppressedBy:
                return new ErrorMessage(strings.SessionSuppressedByOther);

            case SessionErrorCode.AccessExpired:
                if (!AppData.IsPremiumSupported)
                    return new ErrorMessage(strings.ServerKeyExpired);
                if (AppData.IsPremiumByAccount)
                    return new ErrorMessage(strings.SubscriptionNotProvisionedMsg);
                return new ErrorMessage(strings.PremiumAccessExpiredMsg, CodeActions());

            case SessionErrorCode.SessionExpired:
                return new ErrorMessage(strings.PremiumConnectionExpiredMsg);
            case SessionErrorCode.DailyLimitExceeded:
                return new ErrorMessage(strings.DailyLimitExceededMsg);

            case SessionErrorCode.AccessCodeRejected:
                if (AppData.IsPremiumByAccount)
                    return new ErrorMessage(strings.SubscriptionNotProvisionedMsg);
                return new ErrorMessage(strings.InvalidAccessCode, CodeActions());

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

    // a refused code is kept; what is offered is what exists (keyring plan §8)
    private static ErrorActions CodeActions()
    {
        return new ErrorActions {
            ShowAccessCodeActions = AppData.State.ClientProfile?.HasAccessCode == true,
            ShowChangeAccessCode = AppData.CanImportAccessCode
        };
    }

    private static ErrorMessage PremiumOnly(IReadOnlyDictionary<string, string?> data)
    {
        var strings = Strings.Current;
        data.TryGetValue("Feature", out var feature);
        return feature switch {
            nameof(AppFeature.QuickLaunch) => new ErrorMessage(strings.QuickLaunchNotSupportedMsg),
            nameof(AppFeature.AlwaysOn) => new ErrorMessage(strings.AlwaysOnNotSupportedMsg),
            _ => new ErrorMessage(data.TryGetValue("Message", out var message) && message != null ? message : strings.UnknownError)
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
