namespace VpnHood.Net.Toolkit.Logging;

public sealed class TraceLoggerProvider(bool includeScopes = true)
    : TextLoggerProvider(new TraceLogger(), includeScopes: includeScopes);
