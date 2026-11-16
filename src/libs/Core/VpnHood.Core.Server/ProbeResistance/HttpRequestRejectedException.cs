namespace VpnHood.Core.Server.ProbeResistance;

// a request Kestrel refuses before any handler sees it, with the status it answers and, for a 405, the method the
// target allows
internal sealed class HttpRequestRejectedException(int statusCode, string? allow = null) : Exception
{
    public int StatusCode => statusCode;
    public string? Allow => allow;
}
