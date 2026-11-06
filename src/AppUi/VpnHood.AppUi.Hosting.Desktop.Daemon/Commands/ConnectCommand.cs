using System.CommandLine;
using VpnHood.AppLib.Api;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.Sessions;
using VpnHood.AppUi.Hosting.Desktop.Daemon.Platform;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Desktop.Daemon.Commands;

// Connect, and by default wait: a command that returned the instant the request was accepted would
// report success for a connection that fails a second later, and every script around it would have
// to poll. --no-wait is there for the caller that wants the old behaviour.
//
// The profile may be named rather than numbered, because a person reading "profile list" sees names
// and a Guid typed by hand is a typo waiting to happen.
//
// It connects even before the window's first-run page is answered: that page gates the window only,
// and no tracker reports until it is answered (AppTrackerService).
internal static class ConnectCommand
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMinutes(2);

    // isAddAccessKeySupported false is a head with one built-in profile (Connect): there is nothing
    // to choose between, so --profile is not offered and the app's own profile is always used.
    public static Command Create(IDesktopPlatform platform, bool isAddAccessKeySupported)
    {
        var profileOption = new Option<string?>("--profile", "-p") {
            Description = "The profile to use, by name or id. Defaults to the one the app is set to."
        };
        var locationOption = new Option<string?>("--location", "-l") {
            Description = "The server location, as 'locations' lists it (for example US or US/Virginia)."
        };
        var noWaitOption = new Option<bool>("--no-wait") {
            Description = "Return as soon as the request is accepted, without waiting for the connection."
        };

        var command = new Command("connect", "Connect the VPN.") { locationOption, noWaitOption };
        if (isAddAccessKeySupported)
            command.Options.Add(profileOption);

        command.SetAction((parseResult, cancellationToken) => DaemonSession.Run(platform,
            (api, token) => Run(api, platform.Paths.CommandName, isAddAccessKeySupported,
                isAddAccessKeySupported ? parseResult.GetValue(profileOption) : null,
                parseResult.GetValue(locationOption),
                parseResult.GetValue(noWaitOption), token),
            cancellationToken));

        return command;
    }

    private static async Task<int> Run(VpnHoodApi api, string commandName, bool isAddAccessKeySupported,
        string? profile, string? location, bool noWait, CancellationToken cancellationToken)
    {
        // Resolved here rather than left to the app: told nothing, the app connects with the
        // profile it is set to and fails with "VpnProfile is not set" when it is set to none -
        // which is every device that has just had its first key added.
        var info = await api.App.GetInfo(cancellationToken).Vhc();
        var vpnProfileId = ProfileLookup.Resolve(info, profile, commandName);

        try {
            await api.App.Connect(vpnProfileId, location, ConnectPlanId.Normal, cancellationToken).Vhc();
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            await PrintFailure(api, commandName, isAddAccessKeySupported, vpnProfileId, ex.Message,
                cancellationToken).Vhc();
            return 1;
        }

        if (noWait)
            return 0;

        var state = await WaitForSettled(api, cancellationToken).Vhc();
        switch (state.ConnectionState) {
            case AppConnectionState.Connected:
            case AppConnectionState.Unstable:
                var where = state.ServerLocationInfo?.ServerLocation;
                await CliPrinter.Line(
                    where == null ? "Connected." : $"Connected. Location: {where}", cancellationToken).Vhc();
                return 0;

            default:
                // the app's own account of it, which is what the window would be showing
                await PrintFailure(api, commandName, isAddAccessKeySupported, vpnProfileId,
                    state.LastError?.Message ?? $"Not connected. State: {state.ConnectionState}",
                    cancellationToken).Vhc();
                return 1;
        }
    }

    // What a server's refusal does not say: a head with no key of its own to add connects on the plan an account or
    // a premium code gives it, and with neither the server only names the plan it was asked for. That refusal gives
    // way to what to do; any other failure keeps its own message, as it may have nothing to do with the plan.
    private static async Task PrintFailure(VpnHoodApi api, string commandName, bool isAddAccessKeySupported,
        Guid vpnProfileId, string message, CancellationToken cancellationToken)
    {
        if (isAddAccessKeySupported ||
            !string.IsNullOrEmpty(await api.VpnProfiles.GetAccessCode(vpnProfileId, cancellationToken).Vhc())) {
            await CliPrinter.Error(message).Vhc();
            return;
        }

        var state = await api.App.GetState(cancellationToken).Vhc();
        var isPlanRejected = state.LastError?.Data.GetValueOrDefault("ErrorCode") == nameof(SessionErrorCode.PlanRejected);
        var isSignedIn = await api.Account.Get(cancellationToken).Vhc() != null;
        if (!isPlanRejected)
            await CliPrinter.Error(message).Vhc();

        var premiumCode = $"use a premium code: {commandName} premium-code <code>";
        if (!isSignedIn)
            await CliPrinter.Error($"Sign in first: {commandName} sign-in, or {premiumCode}").Vhc();
        else if (isPlanRejected)
            await CliPrinter.Error($"This account has no Premium plan. Get one on the account website, or {premiumCode}").Vhc();
    }

    // Until the app stops moving: every state but the ones that mean "still trying" is an answer.
    private static async Task<AppState> WaitForSettled(VpnHoodApi api, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ConnectTimeout);

        while (true) {
            var state = await api.App.GetState(cancellationToken).Vhc();
            if (!IsSettling(state.ConnectionState))
                return state;

            try {
                await Task.Delay(TimeSpan.FromMilliseconds(500), timeout.Token).Vhc();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                return state; // gave it its two minutes; report whatever it was doing
            }
        }
    }

    private static bool IsSettling(AppConnectionState connectionState)
    {
        return connectionState
            is AppConnectionState.Initializing
            or AppConnectionState.WaitingForVpnPermission
            or AppConnectionState.Waiting
            or AppConnectionState.Diagnosing
            or AppConnectionState.ValidatingProxies
            or AppConnectionState.FindingReachableServer
            or AppConnectionState.FindingBestServer
            or AppConnectionState.Connecting;
    }
}
