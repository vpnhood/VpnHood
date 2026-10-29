using System.Net;

namespace VpnHood.Test.KestrelParity.Probing;

// one server as the probes reach it: its TLS port, its plain port and the name its certificate carries
internal sealed record ProbeTarget(string Name, IPEndPoint Tls, IPEndPoint Http, string Sni);
