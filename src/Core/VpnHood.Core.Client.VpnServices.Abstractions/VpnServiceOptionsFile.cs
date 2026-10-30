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

    public async Task<VpnServiceOptions> Read(CancellationToken cancellationToken)
    {
        var json = await FileUtils.WaitOutSharingAsync(() => File.ReadAllTextAsync(FilePath, cancellationToken),
            SharingTimeout, cancellationToken).Vhc();
        return JsonUtils.Deserialize<VpnServiceOptions>(json);
    }

    // null when there is none, or it cannot be read, which it logs
    public async Task<VpnServiceOptions?> TryRead(CancellationToken cancellationToken)
    {
        if (!Exists)
            return null;

        try {
            return await Read(cancellationToken).Vhc();
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
        await WriteFile(serviceOptions, cancellationToken).Vhc();
    }

    // Options made again from the app's settings, connected or not; nothing without a file, which only a
    // connect makes. What only a connect knows - its plan, and whether always-on made it - these options
    // take from the saved ones, read under the lock a connect's write takes, so one in between is kept.
    public async Task Update(VpnServiceOptions serviceOptions, CancellationToken cancellationToken)
    {
        using var scopeLock = await _writeLock.LockAsync(cancellationToken).Vhc();
        if (!Exists)
            return;

        var savedOptions = await Read(cancellationToken).Vhc();
        serviceOptions.ClientOptions.PlanId = savedOptions.ClientOptions.PlanId;
        serviceOptions.ClientOptions.IsAlwaysOn = savedOptions.ClientOptions.IsAlwaysOn;
        await WriteFile(serviceOptions, cancellationToken).Vhc();
    }

    public async Task Delete(CancellationToken cancellationToken)
    {
        using var scopeLock = await _writeLock.LockAsync(cancellationToken).Vhc();
        await FileUtils.WaitOutSharingAsync(() => {
            File.Delete(FilePath);
            return Task.CompletedTask;
        }, SharingTimeout, cancellationToken).Vhc();
    }

    // the token ends the wait between tries, not a write under way: a cut one would leave the file in part
    private async Task WriteFile(VpnServiceOptions serviceOptions, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(serviceOptions);
        await FileUtils.WaitOutSharingAsync(() => File.WriteAllTextAsync(FilePath, json, CancellationToken.None),
            SharingTimeout, cancellationToken).Vhc();
    }
}
