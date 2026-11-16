namespace VpnHood.Core.Proxies.Management;

// the proxy of a connection the managed connector handed out, and when, in Environment.TickCount64
internal sealed record HandedOutConnection(ProxyEndPointEntry Entry, long TickCount);
