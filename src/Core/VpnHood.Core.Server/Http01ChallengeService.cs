using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;
using static VpnHood.Core.Server.Http01ChallengeHandler;

namespace VpnHood.Core.Server;

public class Http01ChallengeService(Http01KeyAuthorizationFunc keyAuthorizationFunc,
    bool throttleRequests = true)
    : IDisposable
{
    public bool ThrottleRequests => throttleRequests;
    private readonly List<Http01ChallengeHandler> _services = [];
    private readonly Lock _servicesLock = new(); // a configuration's start may meet the server's quick close
    private bool _disposed;

    public int Start(IEnumerable<IPAddress> ipAddresses)
    {
        lock (_servicesLock) {
            ObjectDisposedException.ThrowIf(_disposed, this);
            StopServices();

            foreach (var ipAddress in ipAddresses) {
                var service = new Http01ChallengeHandler(ipAddress, keyAuthorizationFunc) {
                    ThrottleRequests = throttleRequests
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