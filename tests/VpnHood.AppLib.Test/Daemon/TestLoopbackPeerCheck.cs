using System.Net;
using VpnHood.AppUi.Hosting.Cli.Channel;

namespace VpnHood.AppLib.Test.Daemon;

// A platform's answer to "is the far end the process I meant", given by the test.
internal sealed class TestLoopbackPeerCheck : ILoopbackPeerCheck
{
    public Func<IPEndPoint, int, bool> Answer { get; set; } = (_, _) => true;

    public Task<bool> IsOwnedBy(IPEndPoint local, IPEndPoint remote, int processId, CancellationToken cancellationToken)
    {
        return Task.FromResult(Answer(remote, processId));
    }
}
