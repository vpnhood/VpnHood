using System.Net;
using VpnHood.AppLogic.Api.WebHost.Helpers;
using VpnHood.Net.Toolkit.Utils;
using WatsonWebserver.Core;
using WatsonWebserver.Lite;

namespace VpnHood.Test.Providers.Proxies;

// A list's URL on this machine: it answers with its content, at once or once released, and counts the downloads
internal sealed class ListServer : IDisposable
{
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly WebserverLite _webServer;
    private int _downloadCount;

    public int Port { get; } = VhUtils.GetFreeTcpEndPoint(IPAddress.Loopback).Port;

    public ListServer(string content, bool isHeld, CancellationToken cancellationToken)
    {
        if (!isHeld)
            _release.TrySetResult();

        _webServer = new WebserverLite(new WebserverSettings(IPAddress.Loopback.ToString(), Port), async ctx => {
            Interlocked.Increment(ref _downloadCount);
            await _release.Task.WaitAsync(cancellationToken);
            await ctx.Response.Send(content);
        });

        _ = _webServer.StartAsync(cancellationToken);
    }

    public Uri Url => new($"http://127.0.0.1:{Port}/list");
    public int DownloadCount => Volatile.Read(ref _downloadCount);
    public void Release() => _release.TrySetResult();

    // a download still held ends with the server
    public void Dispose()
    {
        _release.TrySetCanceled();
        _webServer.TryStop();
    }
}
