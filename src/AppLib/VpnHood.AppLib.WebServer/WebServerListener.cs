using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.WebServer.Helpers;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;
using WatsonWebserver.Lite;

namespace VpnHood.AppLib.WebServer;

// One listener and its whole state machine. The web view's own listener and the remote-access one
// are the same thing bound to different places, so bind, stop and recovery exist once, here. The
// factory decides where a fresh instance binds (the host can depend on a setting); the port is
// fixed per listener so the probe knows where to connect. What differs between the two listeners
// lives in VpnHoodAppWebServer: who creates them, when they go, and who is told they came back.
internal class WebServerListener(string name, int port, Func<WebserverLite> serverFactory) : IDisposable
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);
    private readonly Lock _lock = new();
    private WebserverLite? _server;
    private bool _disposed;

    public int Port => port;

    // The listener's own state. CavemanTcp clears it when its accept loop exits, so false means
    // the listener is gone (or was never started).
    public bool IsListening => _server?.IsListening == true;

    // The lock serializes the state transitions: the initial start, the watchdog and a web view's
    // recovery can all drive them from different threads. Monitor is
    // re-entrant, so Restart()'s nested Stop()/Start() are fine. The listener socket is bound and
    // listening before Start() returns, so a caller can point a web view at it immediately.
    public void Start()
    {
        lock (_lock) {
            ObjectDisposedException.ThrowIf(_disposed, this);
            VhLogger.Instance.LogInformation("Starting the {Name} web server listener on port {Port}...", name, port);
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

            VhLogger.Instance.LogInformation("Stopping the {Name} web server listener...", name);
            server.TryStop();
            server.Dispose();
            _server = null;
        }
    }

    public void Restart()
    {
        lock (_lock) {
            Stop();
            Start();
        }
    }

    // Watchdog step: put the listener back when it has died. It only reads the listener's own
    // state, so a healthy listener is never restarted. Returns whether it restarted.
    public bool RestartIfDown()
    {
        lock (_lock) {
            if (_disposed || _server == null || IsListening)
                return false;

            VhLogger.Instance.LogWarning("The {Name} web server listener is down; restarting it.", name);
            Restart();
            return true;
        }
    }

    // Probe step, for concrete signals only (a resume, a web view that failed to connect), never
    // periodically: a probe that times out on a busy system would restart a healthy listener. iOS
    // can close a socket during a suspension while the accept loop still believes it is listening,
    // so the flag alone is not enough there. The probe awaits, so it runs outside the lock; the
    // instance it judged is compared under it, so two overlapping signals restart once, not twice.
    public async Task<bool> RestartIfUnreachable()
    {
        var server = _server;
        if (_disposed || server == null || await IsReachable().Vhc())
            return false;

        lock (_lock) {
            if (_disposed || !ReferenceEquals(_server, server))
                return false; // gone, or already restarted by the other signal

            VhLogger.Instance.LogWarning("The {Name} web server listener is not reachable; restarting it.", name);
            Restart();
            return true;
        }
    }

    private async Task<bool> IsReachable()
    {
        try {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(ProbeTimeout);
            await client.ConnectAsync(IPAddress.Loopback, port, cts.Token).Vhc();
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
