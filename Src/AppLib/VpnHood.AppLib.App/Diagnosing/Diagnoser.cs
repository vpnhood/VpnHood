using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client;
using VpnHood.Core.Client.Exceptions;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Net;
using VpnHood.Core.Common.Utils;

namespace VpnHood.AppLib.Diagnosing;

public class Diagnoser
{
    private bool _isWorking;

    public IPAddress[] TestPingIpAddresses { get; set; } = [
        IPAddressUtil.KidsSafeCloudflareDnsServers.First(x => x.IsV4()),
        IPAddressUtil.GoogleDnsServers.First(x => x.IsV4())
    ];

    public IPEndPoint[] TestNsIpEndPoints { get; set; } = [
        new(IPAddressUtil.KidsSafeCloudflareDnsServers.First(x => x.IsV4()), 53),
        new(IPAddressUtil.GoogleDnsServers.First(x => x.IsV4()), 53)
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
            VhLogger.Instance.LogInformation("Checking the Internet connection...");
            IsWorking = true;
            if (!await NetworkCheck(successOnAny: true, checkPing: true, checkUdp: true,
                    cancellationToken: cancellationToken).VhConfigureAwait())
                throw new NoInternetException();

            throw;
        }
        finally {
            IsWorking = false;
        }
    }

    public async Task CheckEndPoints(IPEndPoint[] ipEndPoints, CancellationToken cancellationToken)
    {
        // check if there is any endpoint
        if (ipEndPoints.Length == 0)
            throw new ArgumentException("There are no endpoints.", nameof(ipEndPoints));

        // ping server
        VhLogger.Instance.LogInformation("Checking the endpoints via ping...");
        if (!await IPAddressUtil.IsIpv6Supported() && ipEndPoints.Any(x => x.IsV6())) {
            VhLogger.Instance.LogInformation("IpV6 is not supported. Excluding IpV6 addresses");
            ipEndPoints = ipEndPoints.Where(x => !x.Address.IsV6()).ToArray();
        }

        // check ping
        var pingRes = await DiagnoseUtil
            .CheckPing(ipEndPoints.Select(x => x.Address).ToArray(), NsTimeout, cancellationToken)
            .VhConfigureAwait();

        if (pingRes == null)
            VhLogger.Instance.LogInformation("At least an endpoint has been pinged successfully.");
        else
            VhLogger.Instance.LogWarning(
                $"Could not ping any endpoints. EndPointCount: {ipEndPoints.Length}, Error: {pingRes.Message}");
    }

    public async Task CheckPureNetwork(CancellationToken cancellationToken)
    {
        try {
            VhLogger.Instance.LogInformation("Checking the Internet connection...");
            IsWorking = true;
            if (!await NetworkCheck(successOnAny: true, checkPing: true, checkUdp: true, cancellationToken)
                    .VhConfigureAwait())
                throw new NoInternetException();
        }
        finally {
            IsWorking = false;
        }
    }

    public async Task CheckVpnNetwork(CancellationToken cancellationToken)
    {
        try {
            VhLogger.Instance.LogInformation("Checking the Vpn Connection...");
            IsWorking = true;
            await Task.Delay(2000, cancellationToken)
                .VhConfigureAwait(); // connections can not be established on android immediately
            if (!await NetworkCheck(successOnAny: false, checkPing: true, checkUdp: true, cancellationToken)
                    .VhConfigureAwait())
                throw new NoStableVpnException();
            VhLogger.Instance.LogInformation("VPN has been established and tested successfully.");
        }
        finally {
            IsWorking = false;
        }
    }

    private async Task<bool> NetworkCheck(bool successOnAny, bool checkPing, bool checkUdp,
        CancellationToken cancellationToken)
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
            cancellationToken
                .ThrowIfCancellationRequested(); // change aggregate exception to operation canceled exception
            throw;
        }
    }
}