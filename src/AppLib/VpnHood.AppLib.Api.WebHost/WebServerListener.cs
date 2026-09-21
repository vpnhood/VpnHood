using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Api.WebHost.Helpers;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;
using WatsonWebserver.Lite;

namespace VpnHood.AppLib.Api.WebHost;

// One listener bound to one address and port: start it, stop it, say whether it is up. This device's
// own listener and the remote-access ones are the same thing bound to different places, so binding and
// stopping exist once, here. The factory makes a fresh instance for the address and port fixed per
// listener, and the probe connects to that same address, so a listener bound to a LAN address is
// judged there and not on loopback. It never recovers itself: a dead listener is replaced by a new one
// through VpnHoodAppWebHost.BindListeners, which is the only thing that may pick a different port when
// something took this one during the outage. Everything else about a listener - who creates them, when
// they go, and who is told they came back - lives there too.
internal class WebServerListener(string name, IPAddress address, int port, Func<WebserverLite> serverFactory) : IDisposable
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);
    private readonly Lock _lock = new();
    private WebserverLite? _server;
    private bool _disposed;

    public IPAddress Address => address;
    public int Port => port;

    // The listener's own state. CavemanTcp clears it when its accept loop exits, so false means
    // the listener is gone (or was never started).
    public bool IsListening => _server?.IsListening == true;

    // The lock serializes the state transitions: the initial start, the watchdog and a caller's
    // recovery can all drive them from different threads. The listener socket is bound and listening
    // before Start() returns, so a caller can point a browser at it immediately.
    public void Start()
    {
        lock (_lock) {
            ObjectDisposedException.ThrowIf(_disposed, this);
            VhLogger.Instance.LogInformation("Starting the {Name} web server listener on {EndPoint}...", name, new IPEndPoint(address, port));
            _server ??= serverFactory();
            _server.Start();
        }
    }

    public void Stop()
    {
        lock (_lock) {
            var server = _server;
            if (server == null)
                return;

            VhLogger.Instance.LogInformation("Stopping the {Name} web server listener on {EndPoint}...", name, new IPEndPoint(address, port));
            server.TryStop();
            server.Dispose();
            _server = null;
        }
    }

    // One real connect, for concrete signals only (a resume, a page that failed to connect), never
    // periodically: a probe that times out on a busy system would condemn a healthy listener. iOS can
    // close a socket during a suspension while the accept loop still believes it is listening, so
    // IsListening alone is not enough there.
    public async Task<bool> IsReachable()
    {
        try {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(ProbeTimeout);
            var probeAddress = address.Equals(IPAddress.Any) ? IPAddress.Loopback : address;
            await client.ConnectAsync(probeAddress, port, cts.Token).Vhc();
            return client.Connected;
        }
        catch {
            return false;
        }
    }

    public void Dispose()
    {
        lock (_lock) {
            _disposed = true;
            Stop();
        }
    }
}
