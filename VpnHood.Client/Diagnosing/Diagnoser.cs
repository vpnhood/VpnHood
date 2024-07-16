using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Client.Exceptions;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;

namespace VpnHood.Client.Diagnosing;

public class Diagnoser
{
    private bool _isWorking;

    public IPAddress[] TestPingIpAddresses { get; set; } = [IPAddress.Parse("8.8.8.8"), IPAddress.Parse("1.1.1.1")];

    public IPEndPoint[] TestNsIpEndPoints { get; set; } =
        [new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53), new IPEndPoint(IPAddress.Parse("1.1.1.1"), 53)];

    public Uri[] TestHttpUris { get; set; } =
        [new Uri("https://www.google.com"), new Uri("https://www.quad9.net/"), new Uri("https://www.microsoft.com/")];

    public int HttpTimeout { get; set; } = 10 * 1000;
    public int NsTimeout { get; set; } = 10 * 1000;
    public event EventHandler? StateChanged;

    public bool IsWorking {
        get => _isWorking;
        private set {
            if (value == _isWorking) return;
            _isWorking = value;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task Connect(VpnHoodClient vpnHoodClient, CancellationToken cancellationToken)
    {
        try {
            await vpnHoodClient.Connect(cancellationToken).VhConfigureAwait();
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception) {
            VhLogger.Instance.LogTrace("Checking the Internet connection...");
            IsWorking = true;
            if (!await NetworkCheck().VhConfigureAwait())
                throw new NoInternetException();

            throw;
        }
        finally {
            IsWorking = false;
        }
    }

    public async Task Diagnose(VpnHoodClient vpnHoodClient, CancellationToken cancellationToken)
    {
        try {
            VhLogger.Instance.LogTrace("Checking the Internet connection...");
            IsWorking = true;
            if (!await NetworkCheck().VhConfigureAwait())
                throw new NoInternetException();

            // ping server
            VhLogger.Instance.LogTrace("Checking the VpnServer ping...");
            var hostEndPoint = await ServerTokenHelper.ResolveHostEndPoint(vpnHoodClient.Token.ServerToken)
                .VhConfigureAwait();
            var pingRes = await DiagnoseUtil.CheckPing([hostEndPoint.Address], NsTimeout, true).VhConfigureAwait();
            if (pingRes == null)
                VhLogger.Instance.LogTrace("Pinging server is OK.");
            else
                VhLogger.Instance.LogWarning(
                    $"Could not ping server! EndPoint: {VhLogger.Format(hostEndPoint.Address)}, Error: {pingRes.Message}");

            // VpnConnect
            IsWorking = false;
            await vpnHoodClient.Connect(cancellationToken).VhConfigureAwait();

            VhLogger.Instance.LogTrace("Checking the Vpn Connection...");
            IsWorking = true;
            await Task.Delay(2000, cancellationToken)
                .VhConfigureAwait(); // connections can not be established on android immediately
            if (!await NetworkCheck().VhConfigureAwait())
                throw new NoStableVpnException();
            VhLogger.Instance.LogTrace("VPN has been established and tested successfully.");
        }
        finally {
            IsWorking = false;
        }
    }

    private async Task<bool> NetworkCheck(bool checkPing = true, bool checkUdp = true)
    {
        var taskPing = checkPing
            ? DiagnoseUtil.CheckPing(TestPingIpAddresses, NsTimeout)
            : Task.FromResult((Exception?)null);

        var taskUdp = checkUdp
            ? DiagnoseUtil.CheckUdp(TestNsIpEndPoints, NsTimeout)
            : Task.FromResult((Exception?)null);

        var taskHttps = DiagnoseUtil.CheckHttps(TestHttpUris, HttpTimeout);

        await Task.WhenAll(taskPing, taskUdp, taskHttps).VhConfigureAwait();
        var hasInternet = taskPing.Result == null && taskUdp.Result == null && taskHttps.Result == null;
        return hasInternet;
    }
}