namespace VpnHood.Net.Toolkit.Logging;

/// <summary>
/// Provides the trace's logger, as a <see cref="TextLoggerProvider"/> over a <see cref="TraceLogger"/>.
/// </summary>
/// <param name="includeScopes"><see langword="true"/> to write the level and the scopes.</param>
/// <param name="includeTime"><see langword="true"/> to write the time.</param>
public sealed class TraceLoggerProvider(bool includeScopes = true, bool includeTime = true)
    : TextLoggerProvider(new TraceLogger(), includeTime: includeTime, includeScopes: includeScopes);
