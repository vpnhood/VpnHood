using System.Collections.Concurrent;
using Android.Content;
using Android.OS;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.Core.Client.Devices.Droid.Messaging;

// Binder-based IMessageClient. It keeps a single dormant binding (no AutoCreate) to
// AndroidVpnService, so it never keeps the VPN service process alive by itself; the binding
// connects whenever the service is started and reconnects automatically after a restart.
// Discovery, endpoints and API keys do not exist here — the binder is obtained by component.
//
// Requests are oneway transactions correlated by requestId; the response arrives on our reply
// binder as a confirmed transaction, so no thread blocks while a request handler runs. The
// transport imposes no request deadline — request duration belongs to the caller's token; only
// connection establishment has a timeout, and service death fails pending requests through the
// disconnect callbacks.
public sealed class AndroidMessageClient : IMessageClient
{
    private static readonly TimeSpan BindTimeout = TimeSpan.FromSeconds(4);
    private readonly object _connectionLock = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<Memory<byte>>> _pendingRequests = new();
    private readonly MessageServiceConnection _serviceConnection;
    private readonly ReplyBinder _replyBinder;
    private TaskCompletionSource<IBinder> _binderTcs = NewBinderTcs();
    private bool _bindRequested;
    private bool _disposed;
    private int _lastRequestId;

    public AndroidMessageClient()
    {
        _serviceConnection = new MessageServiceConnection(this);
        _replyBinder = new ReplyBinder(this);
    }

    private static TaskCompletionSource<IBinder> NewBinderTcs() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<Memory<byte>> SendAsync(Memory<byte> request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var binder = await GetBinder(cancellationToken).Vhc();

        // register before sending so the reply can never race the registration
        var requestId = Interlocked.Increment(ref _lastRequestId);
        var tcs = new TaskCompletionSource<Memory<byte>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[requestId] = tcs;

        try {
            SendRequest(binder, requestId, request);

            // no transport deadline: replies are confirmed transactions and service death fails
            // pending requests via the disconnect callbacks, so the caller's token rules the wait
            return await tcs.Task.WaitAsync(cancellationToken).Vhc();
        }
        finally {
            _pendingRequests.TryRemove(requestId, out _);
        }
    }

    private void SendRequest(IBinder binder, int requestId, Memory<byte> request)
    {
        var data = Parcel.Obtain();
        try {
            data.WriteInterfaceToken(AndroidMessageTransport.InterfaceToken);
            data.WriteInt(requestId);
            data.WriteStrongBinder(_replyBinder);
            AndroidMessageTransport.WriteBlob(data, request);

            if (!binder.Transact(AndroidMessageTransport.RequestTransactionCode, data, null,
                    TransactionFlags.Oneway))
                throw new VpnServiceUnreachableException("VpnService did not accept the request.");
        }
        catch (Exception ex) when (ex is not VpnServiceUnreachableException) {
            throw new VpnServiceUnreachableException("VpnService is unreachable.", ex);
        }
        finally {
            data.Recycle();
        }
    }

    private async Task<IBinder> GetBinder(CancellationToken cancellationToken)
    {
        Task<IBinder> binderTask;
        lock (_connectionLock) {
            // recheck under the lock so a concurrent Dispose cannot leave a fresh binding behind
            ObjectDisposedException.ThrowIf(_disposed, this);

            // a completed-but-dead binder means the service died; wait for the reconnect
            if (_binderTcs.Task is { IsCompletedSuccessfully: true, Result.IsBinderAlive: false })
                _binderTcs = NewBinderTcs();

            if (!_bindRequested) {
                var context = Application.Context;
                var intent = new Intent(context, typeof(AndroidVpnService));
                intent.SetAction(AndroidMessageTransport.BindAction);

                VhLogger.Instance.LogDebug("Binding to the VpnService message channel...");
                if (!context.BindService(intent, _serviceConnection, 0)) {
                    TryUnbind(context);
                    throw new VpnServiceUnreachableException("Could not bind to the VpnService.");
                }

                _bindRequested = true;
            }

            binderTask = _binderTcs.Task;
        }

        try {
            return await binderTask.WaitAsync(BindTimeout, cancellationToken).Vhc();
        }
        catch (TimeoutException ex) {
            throw new VpnServiceUnreachableException("VpnService is unreachable.", ex);
        }
    }

    private void TryUnbind(Context context)
    {
        try {
            context.UnbindService(_serviceConnection);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "Could not unbind the VpnService message channel.");
        }
    }

    private void FailPendingRequests(string message)
    {
        foreach (var requestId in _pendingRequests.Keys)
            if (_pendingRequests.TryRemove(requestId, out var tcs))
                tcs.TrySetException(new VpnServiceUnreachableException(message));
    }

    public void Dispose()
    {
        lock (_connectionLock) {
            if (_disposed)
                return;
            _disposed = true;

            if (_bindRequested) {
                TryUnbind(Application.Context);
                _bindRequested = false;
            }
        }

        FailPendingRequests("VpnService message client has been disposed.");
        _replyBinder.Dispose();
        _serviceConnection.Dispose();
    }

    // receives the service's oneway reply transactions and completes the matching pending request
    private class ReplyBinder(AndroidMessageClient client) : Binder
    {
        protected override bool OnTransact(int code, Parcel data, Parcel? reply, int flags)
        {
            if (code != AndroidMessageTransport.ReplyTransactionCode)
                return base.OnTransact(code, data, reply, flags);

            // only this app's processes may complete pending requests
            if (CallingUid != Process.MyUid())
                throw new Java.Lang.SecurityException(
                    "VpnService message replies are not accepted from other apps.");

            data.EnforceInterface(AndroidMessageTransport.InterfaceToken);
            var requestId = data.ReadInt();
            var success = data.ReadInt() == 1;

            // a missing entry means the request has already timed out or been canceled
            if (!client._pendingRequests.TryRemove(requestId, out var tcs))
                return true;

            if (!success) {
                var errorMessage = data.ReadString() ?? "VpnService could not process the message.";
                tcs.TrySetException(new VpnServiceUnreachableException(errorMessage));
                return true;
            }

            var response = data.CreateByteArray();
            if (response != null)
                tcs.TrySetResult(response);
            else
                tcs.TrySetException(new VpnServiceUnreachableException("VpnService returned an empty response."));
            return true;
        }
    }

    private class MessageServiceConnection(AndroidMessageClient client) : Java.Lang.Object, IServiceConnection
    {
        public void OnServiceConnected(ComponentName? name, IBinder? service)
        {
            VhLogger.Instance.LogDebug("VpnService message channel has been connected.");
            if (service == null)
                return;

            lock (client._connectionLock) {
                if (client._binderTcs.Task.IsCompleted)
                    client._binderTcs = NewBinderTcs();
                client._binderTcs.TrySetResult(service);
            }
        }

        public void OnServiceDisconnected(ComponentName? name)
        {
            VhLogger.Instance.LogDebug("VpnService message channel has been disconnected.");
            lock (client._connectionLock) {
                if (client._binderTcs.Task.IsCompleted)
                    client._binderTcs = NewBinderTcs();
            }

            client.FailPendingRequests("VpnService has been disconnected.");
        }

        public void OnBindingDied(ComponentName? name)
        {
            // the binding is dead for good; release it so the next send creates a fresh one
            VhLogger.Instance.LogDebug("VpnService message channel binding has died.");
            ResetBinding();
            client.FailPendingRequests("VpnService binding has died.");
        }

        public void OnNullBinding(ComponentName? name)
        {
            // the service refused to hand out a binder; release the binding so the next send retries
            VhLogger.Instance.LogDebug("VpnService message channel returned a null binding.");
            ResetBinding();
        }

        private void ResetBinding()
        {
            lock (client._connectionLock) {
                if (client._bindRequested)
                    client.TryUnbind(Application.Context);
                client._bindRequested = false;

                if (client._binderTcs.Task.IsCompleted)
                    client._binderTcs = NewBinderTcs();
            }
        }
    }
}
