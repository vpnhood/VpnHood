using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Server.ProbeResistance;
using VpnHood.Net.Toolkit.Logging;
using static VpnHood.Core.Server.Http01ChallengeHandler;

namespace VpnHood.Core.Server;

public class Http01ChallengeService : IDisposable
{
    private readonly Http01KeyAuthorizationFunc _keyAuthorizationFunc;
    private readonly UnauthenticatedConnections _unauthenticatedConnections;
    private readonly bool _throttleRequests;
    private readonly List<Http01ChallengeHandler> _services = [];
    private readonly Lock _servicesLock = new(); // a configuration's start may meet the server's quick close
    private bool _disposed;

    public bool ThrottleRequests => _throttleRequests;

    // a port 80 of its own, as one that only answers challenges has
    public Http01ChallengeService(Http01KeyAuthorizationFunc keyAuthorizationFunc, bool throttleRequests = true)
        : this(keyAuthorizationFunc, new UnauthenticatedConnections(), throttleRequests)
    {
    }

    // the server's port 80, whose connections count with its own that haven't proven a key
    internal Http01ChallengeService(Http01KeyAuthorizationFunc keyAuthorizationFunc,
        UnauthenticatedConnections unauthenticatedConnections, bool throttleRequests)
    {
        _keyAuthorizationFunc = keyAuthorizationFunc;
        _unauthenticatedConnections = unauthenticatedConnections;
        _throttleRequests = throttleRequests;
    }

    // port 80 on each address, redirecting to HTTPS's own port
    public int Start(IEnumerable<IPAddress> ipAddresses)
    {
        return Start(ipAddresses.Select(x => new IPEndPoint(x, 443)));
    }

    // port 80 on the address of each of the server's TLS endpoints, redirecting there; to 443 where the server
    // listens on it, as a web server's HTTPS port
    public int Start(IEnumerable<IPEndPoint> tlsEndPoints)
    {
        lock (_servicesLock) {
            ObjectDisposedException.ThrowIf(_disposed, this);
            StopServices();

            foreach (var group in tlsEndPoints.GroupBy(x => x.Address)) {
                var ipAddress = group.Key;
                var httpsPort = group.Any(x => x.Port == 443) ? 443 : group.First().Port;
                var service = new Http01ChallengeHandler(ipAddress, httpsPort, _keyAuthorizationFunc,
                    _unauthenticatedConnections) {
                    ThrottleRequests = _throttleRequests
                };

                try {
                    service.Start();
                    _services.Add(service);
                }
                catch (Exception ex) {
                    VhLogger.Instance.LogError(ex, "Could not start HTTP-01 Challenge Listener on {EndPoint}",
                        ipAddress);
                    service.Dispose();
                }
            }

            return _services.Count;
        }
    }

    public void Stop()
    {
        lock (_servicesLock) {
            if (!_disposed)
                StopServices();
        }
    }

    private void StopServices()
    {
        foreach (var service in _services)
            service.Dispose();

        _services.Clear();
    }

    public void Dispose()
    {
        lock (_servicesLock) {
            if (_disposed)
                return;

            _disposed = true;
            StopServices();
        }
    }
}
