using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Diagnosing;

public class DiagnoseUtil
{
    public static Task<Exception?> CheckHttps(Uri[] uris, int timeout, CancellationToken cancellationToken)
    {
        var tasks = uris.Select(x => CheckHttps(x, timeout, cancellationToken));
        return WhenAnySuccess(tasks.ToArray());
    }

    public static Task<Exception?> CheckUdp(IPEndPoint[] nsIpEndPoints, int timeout,
        CancellationToken cancellationToken)
    {
        var tasks = nsIpEndPoints.Select(x => CheckUdp(x, timeout, cancellationToken));
        return WhenAnySuccess(tasks.ToArray());
    }

    public static Task<Exception?> CheckPing(IPAddress[] ipAddresses, int timeout, CancellationToken cancellationToken)
    {
        var tasks = ipAddresses.Select(x => CheckPing(x, timeout, cancellationToken));
        return WhenAnySuccess(tasks.ToArray());
    }

    private static async Task<Exception?> WhenAnySuccess(Task<Exception?>[] tasks)
    {
        Exception? exception = null;
        while (tasks.Length > 0) {
            var task = await Task.WhenAny(tasks).VhConfigureAwait();
            exception = task.Result;
            if (exception == null)
                return null; //at least one task is success

            tasks = tasks.Where(x => x != task).ToArray();
        }

        return exception;
    }

    public static async Task<Exception?> CheckHttps(Uri uri, int timeout, CancellationToken cancellationToken)
    {
        try {
            VhLogger.Instance.LogInformation(
                "HttpTest: {HttpTestStatus}, Url: {url}, Timeout: {timeout}...",
                "Started", uri, timeout);

            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMilliseconds(timeout);
            var result = await httpClient.GetStringAsync(uri)
                .VhWait(timeout, cancellationToken)
                .VhConfigureAwait();

            if (result.Length < 100)
                throw new Exception("The http response data length is not expected!");

            VhLogger.Instance.LogInformation(
                "HttpTest: {HttpTestStatus}, Url: {url}.",
                "Succeeded", uri);

            return null;
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(
                "HttpTest: {HttpTestStatus}!, Url: {url}. Message: {ex.Message}",
                "Failed", uri, ex.Message);
            return ex;
        }
    }

    public static async Task<Exception?> CheckUdp(IPEndPoint nsIpEndPoint, int timeout,
        CancellationToken cancellationToken)
    {
        using var udpClient = new UdpClient();
        const string dnsName = "www.google.com";
        try {
            VhLogger.Instance.LogInformation(
                "UdpTest: {UdpTestStatus}, DnsName: {DnsName}, NsServer: {NsServer}, Timeout: {Timeout}...",
                "Started", dnsName, nsIpEndPoint, timeout);

            var res = await DnsResolver.GetHostEntry(dnsName, nsIpEndPoint, udpClient, timeout, cancellationToken)
                .VhWait(cancellationToken)
                .VhConfigureAwait();

            if (res.AddressList.Length == 0)
                throw new Exception("Could not find any host!");

            VhLogger.Instance.LogInformation(
                "UdpTest: {UdpTestStatus}, DnsName: {DnsName}, NsServer: {NsServer}.",
                "Succeeded", dnsName, nsIpEndPoint);

            return null;
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex,
                "UdpTest: {UdpTestStatus}!, DnsName: {DnsName}, NsServer: {NsServer}, Message: {Message}.",
                "Failed", dnsName, nsIpEndPoint, ex.Message);

            return ex;
        }
    }

    public static async Task<Exception?> CheckPing(IPAddress ipAddress, int timeout,
        CancellationToken cancellationToken)
    {
        try {
            using var ping = new Ping();
            VhLogger.Instance.LogInformation(
                "PingTest: {PingTestStatus}, RemoteAddress: {RemoteAddress}, Timeout: {Timeout}...",
                "Started", VhLogger.Format(ipAddress), timeout);

            var pingReply = await ping.SendPingAsync(ipAddress, timeout)
                .VhWait(cancellationToken)
                .VhConfigureAwait();

            if (pingReply.Status != IPStatus.Success)
                throw new Exception($"Status: {pingReply.Status}");

            VhLogger.Instance.LogInformation(
                "PingTest: {PingTestStatus}, RemoteAddress: {RemoteAddress}.",
                "Succeeded", VhLogger.Format(ipAddress));
            return null;
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex,
                "PingTest: {PingTestStatus}!, RemoteAddress: {RemoteAddress}. Message: {Message}",
                "Failed", VhLogger.Format(ipAddress), ex.Message);
            return ex;
        }
    }
}