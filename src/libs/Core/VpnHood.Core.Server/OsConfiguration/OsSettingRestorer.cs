using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Server.OsConfiguration;

// Sets the OS's settings, saving the OS's own value of each before the server's first change, in a file that outlives
// the server, and putting it back where the server no longer wants its own. A value the server did not set, as after an
// admin's change or a restart of the machine, is the OS's own. A failure is logged, and the next configuration tries
// again
internal class OsSettingRestorer(IOsConfigurationProvider osConfigurationProvider, string storagePath)
{
    private readonly string _filePath = Path.Combine(storagePath, "os-originals.json");

    // null puts back the OS's own
    public async Task SetTcpCongestionControl(string? name, CancellationToken cancellationToken)
    {
        try {
            var current = await osConfigurationProvider.GetTcpCongestionControl(cancellationToken).Vhc();
            var originals = await LoadOriginals(cancellationToken).Vhc();
            var original = originals.TcpCongestionControlApplied == current
                ? originals.TcpCongestionControl ?? current
                : current;
            var target = name ?? original;

            // on record before the change, so a change that fails still leaves the OS's own known
            if (target != original) {
                originals.TcpCongestionControl = original;
                originals.TcpCongestionControlApplied = target;
                await SaveOriginals(originals, cancellationToken).Vhc();
            }

            if (target != current) {
                VhLogger.Instance.LogInformation("Updating TcpCongestionControl. From: {Current}, To: {Target}",
                    current, target);
                await osConfigurationProvider.SetTcpCongestionControl(target, cancellationToken).Vhc();
            }

            // off the record once the OS's own holds
            if (target == original && originals.TcpCongestionControl != null) {
                originals.TcpCongestionControl = null;
                originals.TcpCongestionControlApplied = null;
                await SaveOriginals(originals, cancellationToken).Vhc();
            }
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            LogFailure(nameof(OsOptions.TcpCongestionControl), ex);
        }
    }

    // null puts back the OS's own, and so does a max below it: a machine with more memory has a larger table of its own
    public async Task SetConnectionTrackingMax(int? max, CancellationToken cancellationToken)
    {
        try {
            var current = await osConfigurationProvider.GetConnectionTrackingMax(cancellationToken).Vhc();
            var originals = await LoadOriginals(cancellationToken).Vhc();
            var original = originals.ConnectionTrackingMaxApplied == current
                ? originals.ConnectionTrackingMax ?? current
                : current;
            var target = Math.Max(max ?? original, original);

            if (target != original) {
                originals.ConnectionTrackingMax = original;
                originals.ConnectionTrackingMaxApplied = target;
                await SaveOriginals(originals, cancellationToken).Vhc();
            }

            if (target != current) {
                VhLogger.Instance.LogInformation("Updating ConnectionTrackingMax. From: {Current}, To: {Target}",
                    current, target);
                await osConfigurationProvider.SetConnectionTrackingMax(target, cancellationToken).Vhc();
            }

            if (target == original && originals.ConnectionTrackingMax != null) {
                originals.ConnectionTrackingMax = null;
                originals.ConnectionTrackingMaxApplied = null;
                await SaveOriginals(originals, cancellationToken).Vhc();
            }
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            LogFailure(nameof(OsOptions.ConnectionTrackingMax), ex);
        }
    }

    // null puts back the OS's own. In whole seconds, as the OS keeps it, so the value it reads back is the one on record
    public async Task SetConnectionTrackingTcpIdleTimeout(TimeSpan? timeout, CancellationToken cancellationToken)
    {
        try {
            var current = await osConfigurationProvider.GetConnectionTrackingTcpIdleTimeout(cancellationToken).Vhc();
            var originals = await LoadOriginals(cancellationToken).Vhc();
            var original = originals.ConnectionTrackingTcpIdleTimeoutApplied == current
                ? originals.ConnectionTrackingTcpIdleTimeout ?? current
                : current;
            var target = timeout != null ? TimeSpan.FromSeconds((long)timeout.Value.TotalSeconds) : original;

            if (target != original) {
                originals.ConnectionTrackingTcpIdleTimeout = original;
                originals.ConnectionTrackingTcpIdleTimeoutApplied = target;
                await SaveOriginals(originals, cancellationToken).Vhc();
            }

            if (target != current) {
                VhLogger.Instance.LogInformation(
                    "Updating ConnectionTrackingTcpIdleTimeout. From: {Current}, To: {Target}", current, target);
                await osConfigurationProvider.SetConnectionTrackingTcpIdleTimeout(target, cancellationToken).Vhc();
            }

            if (target == original && originals.ConnectionTrackingTcpIdleTimeout != null) {
                originals.ConnectionTrackingTcpIdleTimeout = null;
                originals.ConnectionTrackingTcpIdleTimeoutApplied = null;
                await SaveOriginals(originals, cancellationToken).Vhc();
            }
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            LogFailure(nameof(OsOptions.ConnectionTrackingTcpIdleTimeout), ex);
        }
    }

    // a setting the machine lacks is no failure of the server's
    private static void LogFailure(string setting, Exception ex)
    {
        if (ex is NotSupportedException)
            VhLogger.Instance.LogInformation("{Setting} is left as it is, as this machine does not support it. {Message}",
                setting, ex.Message);
        else
            VhLogger.Instance.LogWarning(ex, "Could not set {Setting}.", setting);
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
            File.Delete(_filePath);
            return;
        }

        Directory.CreateDirectory(storagePath);
        await FileUtils.WriteAllTextAtomicAsync(_filePath, JsonSerializer.Serialize(originals), cancellationToken).Vhc();
    }
}
