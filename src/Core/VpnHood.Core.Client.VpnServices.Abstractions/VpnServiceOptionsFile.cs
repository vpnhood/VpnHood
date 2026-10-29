using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Client.VpnServices.Abstractions;

// The options a VPN service runs with, saved in its config folder. The app writes them at each connect
// and makes them again at each change of its settings or servers; the service reads them at each start,
// including one the app does not make - always-on, the quick-settings tile, the system's VPN settings,
// the system restarting the service - which then runs with what the app shows.
public class VpnServiceOptionsFile(string configFolder)
{
    // the VPN service reads them at its start, in a process of its own, while the app may be writing them
    private static readonly TimeSpan SharingTimeout = TimeSpan.FromSeconds(2);
    private readonly AsyncLock _writeLock = new();

    public string FilePath { get; } = Path.Combine(configFolder, ClientOptions.VpnConfigFileName);
    public bool Exists => File.Exists(FilePath);

    public VpnServiceOptions Read(CancellationToken cancellationToken)
    {
        return FileUtils.WaitOutSharing(() => JsonUtils.DeserializeFile<VpnServiceOptions>(FilePath), SharingTimeout,
            cancellationToken);
    }

    // null when there is none, or it cannot be read, which it logs
    public VpnServiceOptions? TryRead(CancellationToken cancellationToken)
    {
        if (!Exists)
            return null;

        try {
            return Read(cancellationToken);
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not read the VPN service options. FilePath: {FilePath}", FilePath);
            return null;
        }
    }

    public async Task Write(VpnServiceOptions serviceOptions, CancellationToken cancellationToken)
    {
        using var scopeLock = await _writeLock.LockAsync(cancellationToken).Vhc();
        WriteFile(serviceOptions, cancellationToken);
    }

    // Options made again from the app's settings, connected or not; nothing without a file, which only a
    // connect makes. What only a connect knows - its plan, and whether always-on made it - these options
    // take from the saved ones, read under the lock a connect's write takes, so one in between is kept.
    public async Task Update(VpnServiceOptions serviceOptions, CancellationToken cancellationToken)
    {
        using var scopeLock = await _writeLock.LockAsync(cancellationToken).Vhc();
        if (!Exists)
            return;

        var savedClientOptions = Read(cancellationToken).ClientOptions;
        serviceOptions.ClientOptions.PlanId = savedClientOptions.PlanId;
        serviceOptions.ClientOptions.IsAlwaysOn = savedClientOptions.IsAlwaysOn;
        WriteFile(serviceOptions, cancellationToken);
    }

    public async Task Delete(CancellationToken cancellationToken)
    {
        using var scopeLock = await _writeLock.LockAsync(cancellationToken).Vhc();
        FileUtils.WaitOutSharing(() => File.Delete(FilePath), SharingTimeout, cancellationToken);
    }

    private void WriteFile(VpnServiceOptions serviceOptions, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(serviceOptions);
        FileUtils.WaitOutSharing(() => File.WriteAllText(FilePath, json), SharingTimeout, cancellationToken);
    }
}
