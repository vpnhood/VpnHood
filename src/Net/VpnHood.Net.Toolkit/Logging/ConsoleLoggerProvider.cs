namespace VpnHood.Net.Toolkit.Logging;

public sealed class ConsoleLoggerProvider(bool includeScopes = true, bool includeTime = true)
    : TextLoggerProvider(new ConsoleLogger(), includeTime: includeTime, includeScopes: includeScopes);
