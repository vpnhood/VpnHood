using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using static VpnHood.Core.Server.Http01ChallengeHandler;

namespace VpnHood.Core.Server;

public class Http01ChallengeService(Http01KeyAuthorizationFunc keyAuthorizationFunc)
    : IDisposable
{
    private readonly List<Http01ChallengeHandler> _services = [];
    private bool _disposed;

    public int Start(IPAddress[] ipAddresses)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Stop();

        foreach (var ipAddress in ipAddresses) {
            var service = new Http01ChallengeHandler(ipAddress, keyAuthorizationFunc);
            try {
                service.Start();
                _services.Add(service);
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Could not start HTTP-01 Challenge Listener on {EndPoint}", ipAddress);
                service.Dispose();
            }
        }

        return _services.Count;
    }

    public void Stop()
    {
        if (_disposed)
            return;

        foreach (var service in _services)
            service.Dispose();

        _services.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Stop();
        _disposed = true;
    }
}