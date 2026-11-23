namespace VpnHood.Core.Proxies.Management;

// the proxy of a connection the managed connector handed out, when, in Environment.TickCount64, and the count of the
// statuses' resets then: a success counted before a later reset is no longer there to take back
internal sealed record HandedOutConnection(ProxyEndPointEntry Entry, long TickCount, long StatusResetCount);
