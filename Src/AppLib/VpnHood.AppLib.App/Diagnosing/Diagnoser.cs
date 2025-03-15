using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Diagnosing;

public class Diagnoser
{
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
    
    private bool _isWorking;
    public bool IsWorking {
        get => _isWorking;
        private set {
            if (value == _isWorking) return;
            _isWorking = value;
            Task.Run(()=>StateChanged?.Invoke(this, EventArgs.Empty));
        }
    }

    public async Task CheckEndPoints(IPEndPoint[] ipEndPoints, CancellationToken cancellationToken)
    {
        IsWorking = true;
        using var workScope = new AutoDispose(() => IsWorking = false);

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
        VhLogger.Instance.LogInformation("Checking the Internet connection...");
        if (!await NetworkCheck(successOnAny: true, checkPing: true, checkUdp: true, cancellationToken).VhConfigureAwait())
            throw new NoInternetException();
    }

    public async Task CheckVpnNetwork(CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation("Checking the Vpn Connection...");
        await Task.Delay(2000, cancellationToken).VhConfigureAwait(); // connections can not be established on android immediately
        if (!await NetworkCheck(successOnAny: false, checkPing: true, checkUdp: true, cancellationToken).VhConfigureAwait())
            throw new NoStableVpnException();
        VhLogger.Instance.LogInformation("VPN has been established and tested successfully.");
    }

    private async Task<bool> NetworkCheck(bool successOnAny, bool checkPing, bool checkUdp,
        CancellationToken cancellationToken)
    {
        IsWorking = true;
        using var workScope = new AutoDispose(() => IsWorking = false);

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