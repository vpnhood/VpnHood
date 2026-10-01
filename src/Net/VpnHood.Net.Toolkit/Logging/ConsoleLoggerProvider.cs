namespace VpnHood.Net.Toolkit.Logging;

public sealed class ConsoleLoggerProvider(bool includeScopes = true)
    : TextLoggerProvider(new ConsoleLogger(), includeScopes: includeScopes);
