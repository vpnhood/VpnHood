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
///         the error to <c>config-error.json</c> so the strike survives process restarts.
///         After the first <see cref="RecordError"/> call, the tracker evaluates whether
///         the pause threshold has been reached.</item>
///   <item>Before each retry, check <see cref="IsPaused"/>. When paused, the tracker blocks
///         all retries except one attempt per <see cref="RetryInterval"/>.</item>
///   <item><see cref="IsPaused"/> always returns false until the first <see cref="RecordError"/>
///         call in the current process, ensuring the server gets at least one attempt after
///         every restart.</item>
///   <item>On a successful configuration, call <see cref="RecordSuccess"/> to clear the
///         strike file, reset the tracker, and resume normal operation.</item>
/// </list>
///
/// <b>Pause behavior:</b>
/// <para>
/// The tracker enters a paused state when the first recorded error is older than
/// <see cref="StrikeDuration"/>, the strike file cannot be read, or the strike file
/// cannot be written. While paused, the server is allowed exactly one retry attempt
/// per <see cref="RetryInterval"/> to give it a chance to recover automatically
/// (e.g., after a transient network issue or access server restart).
/// </para>
///
/// This class is designed to be called from a single thread and is not thread-safe.
/// </summary>
public class ConfigErrorTracker
{
    /// <summary>The duration after which persistent errors cause the tracker to pause retries.</summary>
    public TimeSpan StrikeDuration { get; }

    /// <summary>While paused, the minimum interval between retry attempts.</summary>
    public TimeSpan RetryInterval { get; }

    public const string StrikeFileName = "config-error.json";
    private readonly string _filePath;
    private ConfigErrorStrike? _strike;
    private bool _hasReachedPauseThreshold;

    public ConfigErrorTracker(string storagePath, TimeSpan strikeDuration, TimeSpan retryInterval)
    {
        StrikeDuration = strikeDuration;
        RetryInterval = retryInterval;
        _filePath = Path.Combine(storagePath, StrikeFileName);
        _strike = LoadFromFile();
    }

    /// <summary>
    /// Returns true when the tracker is in a paused state and retries should be skipped.
    /// This property is idempotent — it does not mutate state.
    /// Always returns false until the first <see cref="RecordError"/> call in the current process,
    /// so the server always gets at least one attempt per process lifetime.
    /// After an error is recorded and the pause threshold is reached, returns true until
    /// <see cref="RetryInterval"/> has elapsed since the last recorded error.
    /// </summary>
    public bool IsPaused {
        get {
            if (!_hasReachedPauseThreshold || _strike == null)
                return false;

            // Allow retry when enough time has passed since the last recorded error.
            return DateTime.UtcNow - _strike.LastErrorTime < RetryInterval;
        }
    }

    /// <summary>
    /// Records a configuration or status error. Persists the strike to disk immediately.
    /// After recording, checks whether the pause threshold has been reached based on the
    /// persisted first error time. If the file cannot be written, the tracker enters the paused state.
    /// </summary>
    public void RecordError(Exception error)
    {
        _strike ??= new ConfigErrorStrike { FirstErrorTime = DateTime.UtcNow };
        _strike.LastErrorTime = DateTime.UtcNow;
        _strike.LastErrorMessage = error.Message;

        try {
            var json = JsonSerializer.Serialize(_strike);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogCritical(ex,
                "Could not write config error strike file. " +
                "Server will pause retries to prevent zombie behavior.");
            _hasReachedPauseThreshold = true;
            return;
        }

        // Check if the strike has reached the pause threshold after recording the error.
        _hasReachedPauseThreshold = DateTime.UtcNow - _strike.FirstErrorTime >= StrikeDuration;
        if (_hasReachedPauseThreshold) {
            VhLogger.Instance.LogCritical(
                "Configuration has been failing since {FirstErrorTime} (UTC), exceeding the {Days}-day threshold. " +
                "Server will pause retries and allow one attempt per {RetryInterval}.",
                _strike.FirstErrorTime, StrikeDuration.Days, RetryInterval);
            _hasReachedPauseThreshold = true;
        }
    }

    /// <summary>
    /// Clears the strike after a successful configuration.
    /// Resets the tracker to normal operation.
    /// </summary>
    public void RecordSuccess()
    {
        _strike = null;
        _hasReachedPauseThreshold = false;
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
    /// so that the tracker immediately enters the paused state.
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
                "Server will pause retries to prevent zombie behavior.");

            // Return a strike that is guaranteed to exceed the threshold
            return new ConfigErrorStrike {
                FirstErrorTime = DateTime.MinValue,
                LastErrorTime = DateTime.UtcNow,
                LastErrorMessage = $"Strike file was unreadable: {ex.Message}"
            };
        }
    }

    private class ConfigErrorStrike
    {
        public DateTime FirstErrorTime { get; init; }
        public DateTime LastErrorTime { get; set; }
        // ReSharper disable once UnusedAutoPropertyAccessor.Local
        public string? LastErrorMessage { get; set; }
    }
}
