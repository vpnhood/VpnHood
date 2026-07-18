using Android.Content;
using Android.OS;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.Core.Client.Devices.Droid.Messaging;

// Binder-based IMessageListener. AndroidVpnService hands out Binder from OnBind for
// AndroidMessageTransport.BindAction. Requests arrive as oneway transactions, so OnTransact only
// dispatches the async handler and returns; the response goes back through the client's reply
// binder as another oneway transaction. Other apps can never reach this binder — the service is
// Exported=false and guarded by BIND_VPN_SERVICE (a signature permission only the system holds),
// while our own processes bind through the same-uid exemption. The uid check below is defense in
// depth so a future manifest change cannot silently open the channel.
public sealed class AndroidMessageListener : IMessageListener
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private MessageHandler? _messageHandler;
    private bool _disposed;

    private readonly IBinder _binder;

    public AndroidMessageListener()
    {
        _binder = new MessageBinder(this);
        VhLogger.Instance.LogDebug("AndroidMessageListener has been created.");
    }

    // claims the message-channel bind and returns its binder; null for any other intent (such as
    // the system's android.net.VpnService bind), which the service must pass to base.OnBind
    public IBinder? TryBind(Intent? intent)
    {
        if (intent?.Action != AndroidMessageTransport.BindAction)
            return null;

        VhLogger.Instance.LogDebug("VpnService message channel is being bound.");
        return _binder;
    }

    public Task Start(MessageHandler messageHandler, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        _messageHandler = messageHandler;
        return Task.CompletedTask;
    }

    private async Task ProcessMessage(int requestId, IBinder replyBinder, byte[] request)
    {
        try {
            var handler = _messageHandler;
            if (_disposed || handler == null)
                throw new InvalidOperationException("VpnService message listener is not started.");

            var response = await handler(request, _cancellationTokenSource.Token).Vhc();
            SendReply(replyBinder, requestId, response, errorMessage: null);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "Could not handle a VpnService message. RequestId: {RequestId}",
                requestId);
            SendReply(replyBinder, requestId, response: default, errorMessage: ex.Message);
        }
    }

    private static void SendReply(IBinder replyBinder, int requestId, Memory<byte> response, string? errorMessage)
    {
        var data = Parcel.Obtain();
        try {
            data.WriteInterfaceToken(AndroidMessageTransport.InterfaceToken);
            data.WriteInt(requestId);
            data.WriteInt(errorMessage == null ? 1 : 0);
            if (errorMessage == null)
                AndroidMessageTransport.WriteBlob(data, response);
            else
                data.WriteString(errorMessage);

            // confirmed (non-oneway) transact: the client only completes a TaskCompletionSource,
            // so the wait is negligible, and a delivery failure surfaces here instead of leaving
            // the client waiting for a reply that never arrives
            replyBinder.Transact(AndroidMessageTransport.ReplyTransactionCode, data, null, 0);
        }
        catch (Exception ex) {
            // the client process is gone; its pending request fails via its disconnect callbacks
            VhLogger.Instance.LogDebug(ex, "Could not deliver a VpnService message reply. RequestId: {RequestId}",
                requestId);
        }
        finally {
            data.Recycle();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _cancellationTokenSource.TryCancel();
        _cancellationTokenSource.TryDispose();
        _messageHandler = null;
    }

    private class MessageBinder(AndroidMessageListener listener) : Binder
    {
        protected override bool OnTransact(int code, Parcel data, Parcel? reply, int flags)
        {
            if (code != AndroidMessageTransport.RequestTransactionCode)
                return base.OnTransact(code, data, reply, flags);

            try {
                // reject any caller that is not this app, regardless of manifest configuration
                if (CallingUid != Process.MyUid())
                    throw new Java.Lang.SecurityException("VpnService messages are not accepted from other apps.");

                data.EnforceInterface(AndroidMessageTransport.InterfaceToken);
                var requestId = data.ReadInt();
                var replyBinder = data.ReadStrongBinder() ??
                                  throw new Java.Lang.IllegalArgumentException(
                                      "VpnService message has no reply binder.");
                var request = data.CreateByteArray() ??
                              throw new Java.Lang.IllegalArgumentException("VpnService message has no payload.");

                // return before the handler completes; the reply goes back as its own oneway transaction
                _ = listener.ProcessMessage(requestId, replyBinder, request);
                return true;
            }
            catch (Exception ex) {
                // oneway transaction: there is no reply parcel to report into; log and drop
                VhLogger.Instance.LogDebug(ex, "Could not accept a VpnService message transaction.");
                return true;
            }
        }
    }
}
