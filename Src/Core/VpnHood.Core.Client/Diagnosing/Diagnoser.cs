using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Exceptions;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Net;
using VpnHood.Core.Common.Utils;

namespace VpnHood.Core.Client.Diagnosing;

public class Diagnoser
{
    private bool _isWorking;

    public IPAddress[] TestPingIpAddresses { get; set; } = [
        IPAddressUtil.KidsSafeCloudflareDnsServers.First(x=>x.IsV4()),
        IPAddressUtil.GoogleDnsServers.First(x=>x.IsV4())
    ];

    public IPEndPoint[] TestNsIpEndPoints { get; set; } = [
        new(IPAddressUtil.KidsSafeCloudflareDnsServers.First(x=>x.IsV4()), 53),
        new(IPAddressUtil.GoogleDnsServers.First(x=>x.IsV4()), 53)
    ];

    public Uri[] TestHttpUris { get; set; } =
        [new("https://weather.com/"), new("https://www.who.int/"), new("https://www.microsoft.com/")];

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
            if (!await NetworkCheck(successOnAny: true, checkPing: true, checkUdp: true, cancellationToken: cancellationToken).VhConfigureAwait())
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
            if (!await NetworkCheck(successOnAny: true, checkPing: true, checkUdp: true, cancellationToken).VhConfigureAwait())
                throw new NoInternetException();

            // ping server
            VhLogger.Instance.LogTrace("Checking the VpnServer ping...");
            var hostEndPoints = await ServerTokenHelper.ResolveHostEndPoints(vpnHoodClient.Token.ServerToken, cancellationToken).VhConfigureAwait();
            if (!await IPAddressUtil.IsIpv6Supported() && hostEndPoints.Any(x => x.IsV6())) {
                VhLogger.Instance.LogTrace("IpV6 is not supported. Excluding IpV6 addresses");
                hostEndPoints = hostEndPoints.Where(x => !x.Address.IsV6()).ToArray();
            }

            var hostEndPoint = hostEndPoints.First() ?? throw new Exception("Could not resolve any host endpoint from AccessToken."); 
            var pingRes = await DiagnoseUtil.CheckPing([hostEndPoint.Address], NsTimeout, cancellationToken).VhConfigureAwait();
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
            await Task.Delay(2000, cancellationToken).VhConfigureAwait(); // connections can not be established on android immediately
            if (!await NetworkCheck(successOnAny: false, checkPing: true, checkUdp: true, cancellationToken).VhConfigureAwait())
                throw new NoStableVpnException();
            VhLogger.Instance.LogTrace("VPN has been established and tested successfully.");
        }
        finally {
            IsWorking = false;
        }
    }

    private async Task<bool> NetworkCheck(bool successOnAny, bool checkPing, bool checkUdp, CancellationToken cancellationToken)
    {
        var taskPing = checkPing
            ? DiagnoseUtil.CheckPing(TestPingIpAddresses, NsTimeout, cancellationToken)
            : Task.FromResult<Exception?>(null);

        var taskUdp = checkUdp
            ? DiagnoseUtil.CheckUdp(TestNsIpEndPoints, NsTimeout, cancellationToken)
            : Task.FromResult<Exception?>(null);

        using var httpClient = new HttpClient();
        var taskHttps = DiagnoseUtil.CheckHttps(TestHttpUris, HttpTimeout, cancellationToken);

        try {
            await Task.WhenAll(taskPing, taskUdp, taskHttps).VhConfigureAwait();
            var hasInternet = successOnAny
                ? taskPing.Result == null || taskUdp.Result == null || taskHttps.Result == null
                : taskPing.Result == null && taskUdp.Result == null && taskHttps.Result == null;

            return hasInternet;
        }
        catch (Exception) {
            cancellationToken.ThrowIfCancellationRequested(); // change aggregate exception to operation canceled exception
            throw;
        }

    }
}