using System.Net;
using System.Net.Sockets;
using VpnHood.Net.Toolkit.Sockets;

namespace VpnHood.Test.Providers.Proxies;

// the system's sockets, each made, while it holds, only once the test lets them: a window before a check's dial
internal sealed class HeldSocketFactory(CancellationToken cancellationToken) : TestSocketFactory
{
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsHolding { get; set; }
    public TaskCompletionSource Held { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release() => _release.TrySetResult();

    public override TcpClient CreateTcpClient(IPEndPoint ipEndPoint, TcpClientOptions? options = null)
    {
        if (IsHolding) {
            Held.TrySetResult();
            _release.Task.Wait(cancellationToken);
        }

        return base.CreateTcpClient(ipEndPoint, options);
    }
}
