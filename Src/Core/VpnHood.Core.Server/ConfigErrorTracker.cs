using Microsoft.Extensions.Logging;
using System.Text.Json;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Server;

/// <summary>
/// Tracks persistent configuration errors to prevent a zombie server from endlessly
/// retrying a bad configuration and flooding the access server with requests.
///
/// <b>How it works:</b>
/// <list type="bullet">
///   <item>On each configuration failure, call <see cref="RecordError"/>. This persists
///         the error to <c>config-error.json</c> so the strike survives process restarts.</item>
///   <item>After recording (or on startup), call <see cref="ShouldPause"/> to check whether
///         the server must stop retrying. The server should enter the <see cref="ServerState.Paused"/>
///         state and remain there until the process is restarted.</item>
///   <item>On a successful configuration, call <see cref="ReportSuccess"/> to clear the
///         strike file and reset the tracker.</item>
/// </list>
///
/// <b>Pause conditions (any of the following):</b>
/// <list type="bullet">
///   <item>The strike file exists but cannot be read (corrupt or permission issue).</item>
///   <item>The error cannot be written to the strike file (disk full, permission issue).</item>
///   <item>The first recorded error is older than <see cref="StrikeDuration"/> (default 7 days).</item>
/// </list>
///
/// This class is designed to be called from a single thread and is not thread-safe.
/// </summary>
internal class ConfigErrorTracker
{
    /// <summary>The duration after which persistent configuration errors cause the server to pause.</summary>
    public static readonly TimeSpan StrikeDuration = TimeSpan.FromDays(7);

    private readonly string _filePath;
    private ConfigErrorStrike? _strike;

    public ConfigErrorTracker(string storagePath)
    {
        _filePath = Path.Combine(storagePath, "config-error.json");
        _strike = LoadFromFile();
    }

    /// <summary>
    /// Records a configuration error. Persists the strike to disk immediately.
    /// If the file cannot be written, the server should pause to avoid zombie behavior,
    /// so this method returns true to indicate a forced pause.
    /// </summary>
    /// <returns>true if the error could NOT be persisted and the server must pause immediately.</returns>
    public bool RecordError(Exception error)
    {
        _strike ??= new ConfigErrorStrike { FirstErrorTime = DateTime.Now };
        _strike.LastErrorTime = DateTime.Now;
        _strike.LastErrorMessage = error.Message;

        try {
            var json = JsonSerializer.Serialize(_strike);
            File.WriteAllText(_filePath, json);
            return false;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogCritical(ex,
                "Could not write config error strike file. " +
                "Server must pause to prevent zombie behavior.");
            return true;
        }
    }

    /// <summary>
    /// Checks whether the server should enter the Paused state and stop retrying.
    /// All decision logic and logging is handled internally.
    /// </summary>
    public bool ShouldPause()
    {
        if (_strike == null)
            return false;

        if (DateTime.Now - _strike.FirstErrorTime >= StrikeDuration) {
            VhLogger.Instance.LogCritical(
                "Configuration has been failing since {FirstErrorTime} (UTC), exceeding the {Days}-day threshold. " +
                "Server will not retry. Restart the process to attempt configuration again.",
                _strike.FirstErrorTime, StrikeDuration.Days);
            return true;
        }

        VhLogger.Instance.LogWarning(
            "A configuration error strike is active (first error: {FirstErrorTime} UTC). " +
            "Server will keep retrying until the {Days}-day threshold is reached.",
            _strike.FirstErrorTime, StrikeDuration.Days);
        return false;
    }

    /// <summary>
    /// Clears the strike after a successful configuration.
    /// Logs a message if the file cannot be deleted, but does not cause a pause.
    /// </summary>
    public void ReportSuccess()
    {
        _strike = null;
        try {
            if (File.Exists(_filePath))
                File.Delete(_filePath);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not delete config error strike file.");
        }
    }

    /// <summary>
    /// Loads the strike from disk. If the file exists but cannot be read, returns a strike
    /// with <see cref="ConfigErrorStrike.FirstErrorTime"/> set to <see cref="DateTime.MinValue"/>
    /// so that <see cref="ShouldPause"/> will immediately return true.
    /// </summary>
    private ConfigErrorStrike? LoadFromFile()
    {
        try {
            return File.Exists(_filePath) 
                ? JsonUtils.DeserializeFile<ConfigErrorStrike>(_filePath) 
                : null;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogCritical(ex,
                "Config error strike file exists but could not be read. " +
                "Server must pause to prevent zombie behavior.");

            // Return a strike that is guaranteed to exceed the threshold
            return new ConfigErrorStrike {
                FirstErrorTime = DateTime.MinValue,
                LastErrorTime = DateTime.Now,
                LastErrorMessage = $"Strike file was unreadable: {ex.Message}"
            };
        }
    }

    private class ConfigErrorStrike
    {
        public DateTime FirstErrorTime { get; init; }
        // ReSharper disable once UnusedAutoPropertyAccessor.Local
        public DateTime LastErrorTime { get; set; }
        // ReSharper disable once UnusedAutoPropertyAccessor.Local
        public string? LastErrorMessage { get; set; }
    }
}
