using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Server.ProbeResistance;
using VpnHood.Net.Toolkit.Logging;
using static VpnHood.Core.Server.Http01ChallengeHandler;

namespace VpnHood.Core.Server;

/// <summary>
/// Provides a server's HTTP-01 challenge listeners: one on port 80 of each of its addresses, which answers ACME's
/// challenges for the server's certificate and redirects the other requests it accepts to HTTPS.
/// </summary>
public class Http01ChallengeService : IDisposable
{
    private readonly Http01KeyAuthorizationFunc _keyAuthorizationFunc;
    private readonly UnauthenticatedConnections _unauthenticatedConnections;
    private readonly bool _throttleRequests;
    private readonly List<Http01ChallengeHandler> _services = [];
    private readonly Lock _servicesLock = new(); // a configuration's start may meet the server's quick close
    private bool _disposed;

    internal bool ThrottleRequests => _throttleRequests;

    /// <summary>
    /// Initializes a new instance of the <see cref="Http01ChallengeService"/> class, with a port 80 of its own, as one that
    /// only answers challenges has.
    /// </summary>
    /// <param name="keyAuthorizationFunc">The method that gives a challenge token's key authorization.</param>
    /// <param name="throttleRequests">
    /// <see langword="true"/>, the default, to answer one challenge at a time from each IPv4 address or IPv6 /64, across
    /// the process; another that comes meanwhile is not answered as a challenge.
    /// </param>
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

    /// <summary>
    /// Starts a listener on port 80 of each address, which redirects to HTTPS's own port, 443; the listeners it had stop
    /// first.
    /// </summary>
    /// <param name="ipAddresses">The addresses.</param>
    /// <returns>The number of listeners that started; one that could not is logged.</returns>
    /// <exception cref="ObjectDisposedException">The service is disposed.</exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="ipAddresses"/>, or one of its addresses, is <see langword="null"/>.
    /// </exception>
    public int Start(IEnumerable<IPAddress> ipAddresses)
    {
        return Start(ipAddresses.Select(x => new IPEndPoint(x, 443)));
    }

    // port 80 on the address of each of the server's TLS endpoints, redirecting there; to 443 where the server
    // listens on it, as a web server's HTTPS port
    internal int Start(IEnumerable<IPEndPoint> tlsEndPoints)
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

    internal void Stop()
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

    /// <inheritdoc/>
    /// <remarks>It stops the listeners; calling it again does nothing.</remarks>
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
