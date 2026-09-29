using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using VpnHood.AppUi.Hosting.Cli.Abstractions;
using VpnHood.AppUi.Hosting.Cli.Exceptions;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Cli.Channel;

// Sends each call to the daemon's latest address with its latest token, over a connection whose far
// end is checked first; a call refused (401) during a rebind is sent once more with the next token.
internal sealed class DaemonApiHandler : DelegatingHandler
{
    private static readonly TimeSpan TokenChangeWait = TimeSpan.FromSeconds(1);
    private static readonly HttpRequestOptionsKey<int> ProcessIdKey = new("VpnHood.DaemonProcessId");

    private readonly IDaemonAnswerProvider _answerProvider;
    private readonly string _notRunningHint;

    public DaemonApiHandler(IDaemonAnswerProvider answerProvider, ILoopbackPeerCheck peerCheck, string instanceName,
        string notRunningHint)
        : base(CreateInner(peerCheck, instanceName))
    {
        _answerProvider = answerProvider;
        _notRunningHint = notRunningHint;
    }

    private static SocketsHttpHandler CreateInner(ILoopbackPeerCheck peerCheck, string instanceName)
    {
        return new SocketsHttpHandler {
            UseCookies = false, // the loopback listener takes a bearer header, never a cookie
            ConnectCallback = async (context, cancellationToken) => {
                // Pure IPv4, as the listener is: a dual-stack socket reaches 127.0.0.1 as
                // ::ffff:127.0.0.1, which on Linux hides the connection from the check.
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try {
                    await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, context.DnsEndPoint.Port), cancellationToken).Vhc();
                    if (!context.InitialRequestMessage.Options.TryGetValue(ProcessIdKey, out var processId))
                        throw new InvalidOperationException("The request names no process to reach.");

                    var local = socket.LocalEndPoint as IPEndPoint ?? throw new InvalidOperationException("The connection has no local end.");
                    var remote = socket.RemoteEndPoint as IPEndPoint ?? throw new InvalidOperationException("The connection has no remote end.");
                    if (!await peerCheck.IsOwnedBy(local, remote, processId, cancellationToken).Vhc())
                        throw new InvalidOperationException($"{instanceName}'s address is held by another program; nothing was sent to it.");

                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch {
                    socket.Dispose();
                    throw;
                }
            }
        };
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var answer = Require();
        if (request.Content != null)
            await request.Content.LoadIntoBufferAsync(cancellationToken).Vhc(); // so a refused call can be sent again

        var response = await Send(request, answer, cancellationToken).Vhc();
        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        var next = await WaitForNewToken(answer, cancellationToken).Vhc();
        if (next == null)
            return response;

        response.Dispose();
        return await Send(Clone(request), next, cancellationToken).Vhc();
    }

    private DaemonChannelAnswer Require()
    {
        if (_answerProvider.Refusal is { } refusal)
            throw new DaemonRefusedException(refusal);

        return _answerProvider.Current ?? throw new DaemonNotRunningException(_notRunningHint);
    }

    private Task<HttpResponseMessage> Send(HttpRequestMessage request, DaemonChannelAnswer answer, CancellationToken cancellationToken)
    {
        var apiUrl = answer.ApiUrl ?? throw new InvalidOperationException("The service named no address.");
        var requestUri = request.RequestUri ?? throw new InvalidOperationException("The request has no address.");
        request.RequestUri = new UriBuilder(requestUri) { Scheme = apiUrl.Scheme, Host = apiUrl.Host, Port = apiUrl.Port }.Uri;
        request.Headers.Authorization = answer.Token is { } token ? new AuthenticationHeaderValue("Bearer", token) : null;
        request.Options.Set(ProcessIdKey, answer.ProcessId);
        return base.SendAsync(request, cancellationToken);
    }

    // The next answer whose token differs from the one sent, or null when none came in time.
    private async Task<DaemonChannelAnswer?> WaitForNewToken(DaemonChannelAnswer sent, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TokenChangeWait);

        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(object? sender, EventArgs e) => changed.TrySetResult();

        _answerProvider.Changed += OnChanged;
        try {
            while (true) {
                var current = _answerProvider.Current;
                if (current != null && current.Token != sent.Token)
                    return current;

                await changed.Task.WaitAsync(timeout.Token).Vhc();
                changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return null;
        }
        finally {
            _answerProvider.Changed -= OnChanged;
        }
    }

    // A request is sent once; the second try is a copy over the same, already buffered, content.
    private static HttpRequestMessage Clone(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri) {
            Content = request.Content,
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };

        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        return clone;
    }
}
