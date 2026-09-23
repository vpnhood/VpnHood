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

    // written by Dispose, read on binder threads
    private volatile bool _disposed;

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
            SendReply(replyBinder, requestId, response);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "Could not handle a VpnService message. RequestId: {RequestId}",
                requestId);
            SendError(replyBinder, requestId, ex.Message);
        }
    }

    private static void SendReply(IBinder replyBinder, int requestId, Memory<byte> response)
    {
        if (response.Length > AndroidMessageTransport.MaxBlobLength) {
            SendError(replyBinder, requestId,
                $"VpnService response is too large for the message channel. Length: {response.Length}");
            return;
        }

        if (!TryTransactReply(replyBinder, requestId, response, errorMessage: null))
            SendError(replyBinder, requestId, "VpnService could not deliver its response.");
    }

    private static void SendError(IBinder replyBinder, int requestId, string errorMessage)
    {
        if (errorMessage.Length > AndroidMessageTransport.MaxErrorMessageLength)
            errorMessage = errorMessage[..AndroidMessageTransport.MaxErrorMessageLength];

        TryTransactReply(replyBinder, requestId, response: default, errorMessage);
    }

    private static bool TryTransactReply(IBinder replyBinder, int requestId, Memory<byte> response,
        string? errorMessage)
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

            // A oneway reply is buffered while the app process is frozen.
            return replyBinder.Transact(AndroidMessageTransport.ReplyTransactionCode, data, null,
                TransactionFlags.Oneway);
        }
        catch (Exception ex) {
            // the client process is gone; So its pending request fails via its disconnect callbacks
            VhLogger.Instance.LogDebug(ex, "Could not deliver a VpnService message reply. RequestId: {RequestId}",
                requestId);
            return false;
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

            var requestId = 0;
            IBinder? replyBinder = null;
            try {
                // reject any caller that is not this app, regardless of manifest configuration
                if (CallingUid != Process.MyUid())
                    throw new Java.Lang.SecurityException("VpnService messages are not accepted from other apps.");

                data.EnforceInterface(AndroidMessageTransport.InterfaceToken);
                requestId = data.ReadInt();
                replyBinder = data.ReadStrongBinder() ??
                              throw new Java.Lang.IllegalArgumentException(
                                  "VpnService message has no reply binder.");
                var request = data.CreateByteArray() ??
                              throw new Java.Lang.IllegalArgumentException("VpnService message has no payload.");

                // return before the handler completes; the reply goes back as its own oneway transaction
                _ = listener.ProcessMessage(requestId, replyBinder, request);
                return true;
            }
            catch (Exception ex) {
                // oneway transaction: there is no reply parcel to report into, so answer through the
                // reply binder once we have one. Dropping it would leave the client waiting forever.
                VhLogger.Instance.LogDebug(ex, "Could not accept a VpnService message transaction.");
                if (replyBinder != null)
                    SendError(replyBinder, requestId, ex.Message);
                return true;
            }
        }
    }
}
