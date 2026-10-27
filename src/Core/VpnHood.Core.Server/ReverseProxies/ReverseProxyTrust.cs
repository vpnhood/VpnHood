using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Core.Tunneling;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Jobs;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Server.ReverseProxies;

// The senders whose X-Forwarded-For names the client: the providers' published addresses and the listed ranges. A
// provider's list is fetched when it joins the configuration and then daily, as providers change theirs, and kept in a
// file: a server that can't fetch keeps the last list, and one that never had a list trusts none of that provider
internal class ReverseProxyTrust : IDisposable
{
    private readonly Lock _lock = new();
    private readonly string _folderPath;
    private readonly HttpMessageHandler? _httpMessageHandler;
    private readonly Job _refreshJob;
    private readonly EventReporter _ignoredReporter = new(
        "X-Forwarded-For has been ignored.", GeneralEventId.Request);
    private readonly Dictionary<ReverseProxyProvider, IReadOnlyList<IpRange>> _providerIpRanges = new();
    private IReadOnlyList<ReverseProxyProvider> _providers = [];
    private IReadOnlyList<IpRange> _ipRanges = [];
    private volatile IpRangeOrderedList _trustedIpRanges = IpRangeOrderedList.Empty;

    public ReverseProxyTrust(string storagePath, HttpMessageHandler? httpMessageHandler)
    {
        _folderPath = Path.Combine(storagePath, "reverse-proxies");
        _httpMessageHandler = httpMessageHandler;
        _refreshJob = new Job(Refresh, new JobOptions {
            Interval = TimeSpan.FromDays(1),
            Name = nameof(ReverseProxyTrust)
        });
    }

    public void Configure(IReadOnlyList<ReverseProxyProvider> providers, IReadOnlyList<IpRange> ipRanges)
    {
        // a provider new to the configuration is fetched at once, then daily with the rest
        if (Apply(providers, ipRanges))
            _ = RefreshNow(CancellationToken.None);
    }

    public Task RefreshNow(CancellationToken cancellationToken)
    {
        return _refreshJob.RunNow(cancellationToken);
    }

    // the last entry outside the trusted ranges: a proxy appends the address it saw to what its sender wrote, so the
    // entries before it are the sender's own word
    public IPAddress GetClientIp(IPAddress remoteIp, IReadOnlyDictionary<string, string> headers)
    {
        var xForwardedFor = headers.GetValueOrDefault("X-Forwarded-For", "");
        if (string.IsNullOrWhiteSpace(xForwardedFor))
            return remoteIp;

        var trustedIpRanges = _trustedIpRanges;
        if (!trustedIpRanges.Contains(remoteIp)) {
            ReportIgnored(remoteIp, "no reverse proxy list names the sender; a server behind another proxy lists it in ReverseProxyIpRanges.");
            return remoteIp;
        }

        // anyone may run a Worker on Cloudflare, whose script writes the address headers of its own requests: Cloudflare
        // marks those requests after the script has run
        if (headers.ContainsKey("CF-Worker")) {
            ReportIgnored(remoteIp, "a Cloudflare Worker sent the request.");
            return remoteIp;
        }

        var entries = xForwardedFor.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        for (var i = entries.Length - 1; i >= 0; i--) {
            if (!IPAddress.TryParse(entries[i], out var ipAddress))
                return remoteIp;

            if (!trustedIpRanges.Contains(ipAddress))
                return ipAddress;
        }

        return remoteIp;
    }

    private bool Apply(IReadOnlyList<ReverseProxyProvider> providers, IReadOnlyList<IpRange> ipRanges)
    {
        lock (_lock) {
            var hasNewProvider = providers.Any(x => !_providers.Contains(x));
            _providers = providers.Distinct().ToArray();
            _ipRanges = ipRanges.ToArray();

            // until its first fetch, a provider has the list its last fetch left
            foreach (var provider in _providers.Where(x => !_providerIpRanges.ContainsKey(x)))
                _providerIpRanges[provider] = ReadFile(provider);

            Rebuild();
            return hasNewProvider;
        }
    }

    private async ValueTask Refresh(CancellationToken cancellationToken)
    {
        foreach (var provider in GetProviders())
            await TryRefresh(provider, cancellationToken).Vhc();
    }

    private IReadOnlyList<ReverseProxyProvider> GetProviders()
    {
        lock (_lock)
            return _providers;
    }

    private async Task TryRefresh(ReverseProxyProvider provider, CancellationToken cancellationToken)
    {
        try {
            var ipRanges = await Fetch(provider, cancellationToken).Vhc();
            lock (_lock) {
                _providerIpRanges[provider] = ipRanges;
                Rebuild();
            }

            WriteFile(provider, ipRanges);
            VhLogger.Instance.LogInformation(
                "The reverse proxy addresses of {Provider} have been refreshed. Count: {Count}", provider, ipRanges.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex,
                "Could not refresh the reverse proxy addresses of {Provider}; its last list stands, if it has one.",
                provider);
        }
    }

    private async Task<IReadOnlyList<IpRange>> Fetch(ReverseProxyProvider provider, CancellationToken cancellationToken)
    {
        using var httpClient = _httpMessageHandler != null
            ? new HttpClient(_httpMessageHandler, disposeHandler: false)
            : new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(30);
        var json = await httpClient.GetStringAsync(ReverseProxyFeed.GetUrl(provider), cancellationToken).Vhc();
        return ReverseProxyFeed.Parse(provider, json);
    }

    private void Rebuild()
    {
        var ipRanges = _providers
            .SelectMany(x => _providerIpRanges.GetValueOrDefault(x) ?? [])
            .Concat(_ipRanges);
        _trustedIpRanges = new IpRangeOrderedList(ipRanges);
    }

    private string GetFilePath(ReverseProxyProvider provider)
    {
        return Path.Combine(_folderPath, $"{provider.ToString().ToLowerInvariant()}.json");
    }

    private IReadOnlyList<IpRange> ReadFile(ReverseProxyProvider provider)
    {
        var filePath = GetFilePath(provider);
        if (!File.Exists(filePath)) {
            VhLogger.Instance.LogWarning(
                "There is no list of the reverse proxy addresses of {Provider} yet; none of them is trusted before its first fetch.",
                provider);
            return [];
        }

        try {
            return JsonSerializer.Deserialize<IpRange[]>(File.ReadAllText(filePath))
                   ?? throw new FormatException("The file holds no list.");
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex,
                "Could not read the last list of the reverse proxy addresses of {Provider}.", provider);
            return [];
        }
    }

    // through a temporary file, so a write cut short never replaces the last list
    private void WriteFile(ReverseProxyProvider provider, IReadOnlyList<IpRange> ipRanges)
    {
        Directory.CreateDirectory(_folderPath);
        var filePath = GetFilePath(provider);
        var tempFilePath = filePath + ".tmp";
        File.WriteAllText(tempFilePath, JsonSerializer.Serialize(ipRanges));
        File.Move(tempFilePath, filePath, overwrite: true);
    }

    // a server behind a proxy that no list names takes the proxy for every client
    private void ReportIgnored(IPAddress remoteIp, string reason)
    {
        if (_ignoredReporter.Raise())
            VhLogger.Instance.LogWarning(GeneralEventId.Request,
                "X-Forwarded-For from {RemoteIp} has been ignored: {Reason}", VhLogger.Format(remoteIp), reason);
    }

    public void Dispose()
    {
        _refreshJob.Dispose();
        _ignoredReporter.Dispose();
    }
}
