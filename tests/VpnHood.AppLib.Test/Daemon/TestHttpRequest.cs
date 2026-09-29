namespace VpnHood.AppLib.Test.Daemon;

internal sealed record TestHttpRequest(string Method, string Target, IReadOnlyDictionary<string, string> Headers)
{
    public string? Authorization => Headers.GetValueOrDefault("Authorization");
    public string? Host => Headers.GetValueOrDefault("Host");
}
