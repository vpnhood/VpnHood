using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Server.OsConfiguration;

// Sets the OS's settings, saving the OS's own value of each with the server's first change, in a file that outlives
// the server, and putting it back where the server no longer wants its own. A value the server did not set, as after an
// admin's change or a restart of the machine, is the OS's own. A setting left as it was throws, for the service to say
internal class OsSettingRestorer(IOsConfigurationProvider osConfigurationProvider, string storagePath)
{
    private readonly string _filePath = Path.Combine(storagePath, "os-originals.json");

    // null puts back the OS's own
    public async Task SetTcpCongestionControl(string? name, CancellationToken cancellationToken)
    {
        var current = await osConfigurationProvider.GetTcpCongestionControl(cancellationToken).Vhc();
        var originals = await LoadOriginals(cancellationToken).Vhc();
        var original = originals.TcpCongestionControlApplied == current
            ? originals.TcpCongestionControl ?? current
            : current;
        var target = name ?? original;
        if (target != current) {
            VhLogger.Instance.LogInformation("Updating TcpCongestionControl. From: {Current}, To: {Target}",
                current, target);
            await osConfigurationProvider.SetTcpCongestionControl(target, cancellationToken).Vhc();
        }

        // on record only once the change holds, so one that fails leaves the record as it was
        originals.TcpCongestionControl = target != original ? original : null;
        originals.TcpCongestionControlApplied = target != original ? target : null;
        await SaveOriginals(originals, cancellationToken).Vhc();
    }

    // null puts back the OS's own, and so does a max below it: a machine with more memory has a larger table of its own
    public async Task SetConnectionTrackingMax(int? max, CancellationToken cancellationToken)
    {
        if (max != null)
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(max.Value, nameof(max));

        var current = await osConfigurationProvider.GetConnectionTrackingMax(cancellationToken).Vhc();
        var originals = await LoadOriginals(cancellationToken).Vhc();
        var original = originals.ConnectionTrackingMaxApplied == current
            ? originals.ConnectionTrackingMax ?? current
            : current;
        var target = Math.Max(max ?? original, original);
        if (target != current) {
            VhLogger.Instance.LogInformation("Updating ConnectionTrackingMax. From: {Current}, To: {Target}",
                current, target);
            await osConfigurationProvider.SetConnectionTrackingMax(target, cancellationToken).Vhc();
        }

        originals.ConnectionTrackingMax = target != original ? original : null;
        originals.ConnectionTrackingMaxApplied = target != original ? target : null;
        await SaveOriginals(originals, cancellationToken).Vhc();
    }

    // null puts back the OS's own. In whole seconds, as the OS keeps it, so the value it reads back is the one on record
    public async Task SetConnectionTrackingTcpIdleTimeout(TimeSpan? timeout, CancellationToken cancellationToken)
    {
        TimeSpan? wholeTimeout = timeout != null ? TimeSpan.FromSeconds((long)timeout.Value.TotalSeconds) : null;
        if (wholeTimeout != null)
            ArgumentOutOfRangeException.ThrowIfLessThan(wholeTimeout.Value, TimeSpan.FromSeconds(1), nameof(timeout));

        var current = await osConfigurationProvider.GetConnectionTrackingTcpIdleTimeout(cancellationToken).Vhc();
        var originals = await LoadOriginals(cancellationToken).Vhc();
        var original = originals.ConnectionTrackingTcpIdleTimeoutApplied == current
            ? originals.ConnectionTrackingTcpIdleTimeout ?? current
            : current;
        var target = wholeTimeout ?? original;
        if (target != current) {
            VhLogger.Instance.LogInformation(
                "Updating ConnectionTrackingTcpIdleTimeout. From: {Current}, To: {Target}", current, target);
            await osConfigurationProvider.SetConnectionTrackingTcpIdleTimeout(target, cancellationToken).Vhc();
        }

        originals.ConnectionTrackingTcpIdleTimeout = target != original ? original : null;
        originals.ConnectionTrackingTcpIdleTimeoutApplied = target != original ? target : null;
        await SaveOriginals(originals, cancellationToken).Vhc();
    }

    private async Task<OsSettingOriginals> LoadOriginals(CancellationToken cancellationToken)
    {
        var originals = await JsonUtils.TryDeserializeFileAsync<OsSettingOriginals>(_filePath, cancellationToken).Vhc();
        return originals ?? new OsSettingOriginals();
    }

    // the file goes once nothing is on record
    private async Task SaveOriginals(OsSettingOriginals originals, CancellationToken cancellationToken)
    {
        if (originals is
            { TcpCongestionControl: null, ConnectionTrackingMax: null, ConnectionTrackingTcpIdleTimeout: null }) {
            if (File.Exists(_filePath))
                File.Delete(_filePath);
            return;
        }

        Directory.CreateDirectory(storagePath);
        await FileUtils.WriteAllTextAtomicAsync(_filePath, JsonSerializer.Serialize(originals), cancellationToken).Vhc();
    }
}
