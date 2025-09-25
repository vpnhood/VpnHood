using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Common.IpLocations;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Services.Proxies;

public class HostCountryResolver(
    IIpLocationProvider ipLocationProvider)
{
    public async Task<Dictionary<string, string>> GetHostCountries(IEnumerable<string> hosts,
        CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(64);
        var tasks = hosts
            .Distinct()
            .Select(async host => {
                await gate.WaitAsync(cancellationToken);
                try {
                    return await ResolveHostCountry(host, cancellationToken);
                }
                finally {
                    gate.Release();
                }
            })
            .ToArray();

        try {
            await Task.WhenAll(tasks).Vhc();
        }
        catch (Exception) {
            // Swallow aggregate exceptions: we record errors per-host
            // so we don’t want to throw here.
        }

        return tasks
            .Where(x => x.IsCompletedSuccessfully)
            .ToDictionary(kv => kv.Result.Item1, kv => kv.Result.Item2);
    }

    private async Task<(string, string)> ResolveHostCountry(string host, CancellationToken cancellationToken)
    {
        if (!IPAddress.TryParse(host, out var ipAddress)) {
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).Vhc();
            ipAddress =
                addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ??
                addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetworkV6);
        }

        if (ipAddress is null)
            throw new Exception("Could not resolve host to an IP address.");

        var ipLocation = await ipLocationProvider.GetLocation(ipAddress, cancellationToken).Vhc();
        return (host, ipLocation.CountryCode);
    }
}