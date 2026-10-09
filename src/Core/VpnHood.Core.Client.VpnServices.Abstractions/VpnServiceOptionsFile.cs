using System.Text.Json;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Client.VpnServices.Abstractions;

// The options a VPN service runs with, saved in its config folder. The app writes them at each connect
// and puts each reconfigure into them; the service reads them at each start, including one the app does
// not make - always-on, the quick-settings tile, the system restarting the service - which then runs
// with the settings of the last connect and every reconfigure after it.
public class VpnServiceOptionsFile(string configFolder)
{
    private readonly AsyncLock _writeLock = new();

    public string FilePath { get; } = Path.Combine(configFolder, ClientOptions.VpnConfigFileName);
    public bool Exists => File.Exists(FilePath);

    public VpnServiceOptions Read()
    {
        return JsonUtils.DeserializeFile<VpnServiceOptions>(FilePath);
    }

    // null when there is none, or it cannot be read, which it logs
    public VpnServiceOptions? TryRead()
    {
        return JsonUtils.TryDeserializeFile<VpnServiceOptions>(FilePath, logger: VhLogger.Instance);
    }

    public async Task Write(VpnServiceOptions serviceOptions, CancellationToken cancellationToken)
    {
        using var scopeLock = await _writeLock.LockAsync(cancellationToken).Vhc();
        await WriteFile(serviceOptions, cancellationToken).Vhc();
    }

    // The settings a reconfigure carries, as ApiController applies them to a running session; nothing
    // without a file.
    public async Task Reconfigure(ClientReconfigureParams reconfigureParams, CancellationToken cancellationToken)
    {
        using var scopeLock = await _writeLock.LockAsync(cancellationToken).Vhc();
        if (!Exists)
            return;

        var serviceOptions = Read();
        var clientOptions = serviceOptions.ClientOptions;
        clientOptions.UseTcpProxy = reconfigureParams.UseTcpProxy;
        clientOptions.DropUdp = reconfigureParams.DropUdp;
        clientOptions.DropQuic = reconfigureParams.DropQuic;
        clientOptions.ChannelProtocol = reconfigureParams.ChannelProtocol;
        clientOptions.UnroutedIpMode = reconfigureParams.UnroutedIpMode;
        clientOptions.UnsupportedIpV6Mode = reconfigureParams.UnsupportedIpV6Mode;
        clientOptions.AllowAnonymousTracker = reconfigureParams.AllowAnonymousTracker;
        serviceOptions.ProxyOptions = reconfigureParams.ProxyOptions;
        await WriteFile(serviceOptions, cancellationToken).Vhc();
    }

    private Task WriteFile(VpnServiceOptions serviceOptions, CancellationToken cancellationToken)
    {
        return File.WriteAllTextAsync(FilePath, JsonSerializer.Serialize(serviceOptions), cancellationToken);
    }
}
