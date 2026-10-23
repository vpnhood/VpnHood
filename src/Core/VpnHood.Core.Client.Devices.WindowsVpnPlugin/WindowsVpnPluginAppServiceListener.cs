using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using Windows.ApplicationModel.AppService;
using Windows.Foundation.Collections;

namespace VpnHood.Core.Client.Devices.WindowsVpnPlugin;

// The VPN host's control channel: the app's connections to the package's app service, which Windows runs in this
// process. Windows freezes the VPN host between VPN events, and a request over a named pipe waited for the next
// one; an open connection's task keeps the host running instead.
public sealed class WindowsVpnPluginAppServiceListener : IMessageListener
{
    // the package manifest's uap:AppService Name, and the one value of each message
    internal const string AppServiceName = "VpnService";
    internal const string MessageKey = "Message";

    private readonly TaskCompletionSource _startedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _disposedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile MessageHandler? _messageHandler;

    public Task Start(MessageHandler messageHandler, CancellationToken cancellationToken)
    {
        _messageHandler = messageHandler;
        _startedTcs.TrySetResult();
        return Task.CompletedTask;
    }

    // Serves the connection until the app closes it, Windows ends its task, or this session's host is disposed.
    public async Task Serve(AppServiceConnection connection, CancellationToken cancellationToken)
    {
        var closedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnServiceClosed(AppServiceConnection sender, AppServiceClosedEventArgs args) => closedTcs.TrySetResult();

        connection.ServiceClosed += OnServiceClosed;
        connection.RequestReceived += OnRequestReceived;
        try {
            await using var registration = cancellationToken.Register(() => closedTcs.TrySetResult());
            await Task.WhenAny(closedTcs.Task, _disposedTcs.Task).Vhc();
        }
        finally {
            connection.RequestReceived -= OnRequestReceived;
            connection.ServiceClosed -= OnServiceClosed;
            connection.Dispose();
        }
    }

    // A WinRT event handler: an exception escaping it takes down the VPN host.
    private async void OnRequestReceived(AppServiceConnection sender, AppServiceRequestReceivedEventArgs args)
    {
        var deferral = args.GetDeferral();
        try {
            // A listener the app's connection made before its session waits for that session's start; unanswered once the
            // session has ended, so the app finds no session at its next request.
            await Task.WhenAny(_startedTcs.Task, _disposedTcs.Task).Vhc();
            var messageHandler = _messageHandler;
            if (messageHandler is null)
                return;

            if (!args.Request.Message.TryGetValue(MessageKey, out var value) || value is not byte[] request)
                throw new InvalidDataException("The app's message carries no request.");

            var response = await messageHandler(request, CancellationToken.None).Vhc();
            await args.Request.SendResponseAsync(new ValueSet { [MessageKey] = response.ToArray() }).AsTask().Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not answer the app's request.");
        }
        finally {
            deferral.Complete();
        }
    }

    public void Dispose()
    {
        _messageHandler = null;
        _disposedTcs.TrySetResult();
    }
}
