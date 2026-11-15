using VpnHood.AppLib.Abstractions.Ads.AdExceptions;
using VpnHood.AppLib.Abstractions.Billing;
using VpnHood.AppLib.Api.Exceptions;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Net.Toolkit.ApiClients;

namespace VpnHood.AppLib.Test.Tests;

// ExceptionType's wire values are class names written as literals, as the contract cannot see most of the classes.
// This project sees them all, so a renamed class fails here rather than quietly falling back to a generic error in
// every UI.
[TestClass]
public class ExceptionTypeTest
{
    private static readonly Dictionary<ExceptionType, Type> ClassByType = new() {
        [ExceptionType.NoErrorFound] = typeof(NoErrorFoundException),
        [ExceptionType.Maintenance] = typeof(MaintenanceException),
        [ExceptionType.Session] = typeof(SessionException),
        [ExceptionType.Ad] = typeof(AdException),
        [ExceptionType.ShowAd] = typeof(ShowAdException),
        [ExceptionType.ShowAdNoUi] = typeof(ShowAdNoUiException),
        [ExceptionType.LoadAd] = typeof(LoadAdException),
        [ExceptionType.NoInternet] = typeof(NoInternetException),
        [ExceptionType.NoStableVpn] = typeof(NoStableVpnException),
        [ExceptionType.UnreachableServer] = typeof(UnreachableServerException),
        [ExceptionType.UnreachableProxyServer] = typeof(UnreachableProxyServerException),
        [ExceptionType.UnreachableServerLocation] = typeof(UnreachableServerLocationException),
        [ExceptionType.RewardNotEarned] = typeof(RewardNotEarnedException),
        [ExceptionType.VpnServiceNotReady] = typeof(VpnServiceNotReadyException),
        [ExceptionType.VpnServiceUnreachable] = typeof(VpnServiceUnreachableException),
        [ExceptionType.VpnServiceTimeout] = typeof(VpnServiceTimeoutException),
        [ExceptionType.UserCanceled] = typeof(UserCanceledException),
        [ExceptionType.ConnectionTimeout] = typeof(ConnectionTimeoutException),
        [ExceptionType.EndPointDiscovery] = typeof(EndPointDiscoveryException),
        [ExceptionType.PremiumOnly] = typeof(PremiumOnlyException),
        [ExceptionType.AdBlocker] = typeof(AdBlockerException),
        [ExceptionType.RequestQuickLaunch] = typeof(RequestQuickLaunchException),
        [ExceptionType.VpnServiceRevoked] = typeof(VpnServiceRevokedException),
        [ExceptionType.SplitEntry] = typeof(SplitEntryException),
        [ExceptionType.Billing] = typeof(BillingException)
    };

    [TestMethod]
    public void Every_exception_type_names_its_class()
    {
        var failures = new List<string>();

        foreach (var exceptionType in Enum.GetValues<ExceptionType>()) {
            if (!ClassByType.TryGetValue(exceptionType, out var type)) {
                failures.Add($"{exceptionType}: has no class in this test's table.");
                continue;
            }

            if (!type.IsAssignableTo(typeof(Exception)))
                failures.Add($"{exceptionType}: {type.Name} is not an exception.");

            // the name a thrown exception carries in its ApiError, read back as a UI reads it
            var apiError = new ApiError { TypeName = type.Name, Message = "" };
            var readBack = apiError.GetExceptionType();
            if (readBack != exceptionType)
                failures.Add($"{exceptionType}: {type.Name} reads back as {readBack?.ToString() ?? "nothing"}.");
        }

        Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
    }
}
