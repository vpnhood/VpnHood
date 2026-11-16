namespace VpnHood.Net.Toolkit.Logging;

/// <summary>
/// Provides the console's logger, as a <see cref="TextLoggerProvider"/> over a <see cref="ConsoleLogger"/>.
/// </summary>
/// <param name="includeScopes"><see langword="true"/> to write the level and the scopes.</param>
/// <param name="includeTime"><see langword="true"/> to write the time.</param>
public sealed class ConsoleLoggerProvider(bool includeScopes = true, bool includeTime = true)
    : TextLoggerProvider(new ConsoleLogger(), includeTime: includeTime, includeScopes: includeScopes);
