using System.Runtime.InteropServices;
using Android.OS;

namespace VpnHood.Core.Client.Devices.Droid.Messaging;

// Shared contract of the binder-based VpnService message channel. The channel carries the same
// opaque request/response blobs as the other IMessageListener/IMessageClient transports; framing
// and serialization stay in ApiController / VpnServiceManager.
//
// The channel is fully asynchronous: a request is a oneway transaction carrying a requestId, the
// client's reply binder and the request blob; the service dispatches the handler and returns
// immediately. The reply travels back on the reply binder as a confirmed (non-oneway)
// transaction — the client side only completes a TaskCompletionSource, so the confirmation wait
// is negligible, and a delivery failure surfaces in the service instead of silently leaving the
// client waiting. No thread on either side blocks while a request handler runs.
internal static class AndroidMessageTransport
{
    // OnBind action that selects the message channel; any other action (especially the system's
    // android.net.VpnService) must fall through to VpnService.OnBind
    public const string BindAction = "com.vpnhood.core.vpnservice.MESSAGE";

    // parcel interface token verified on both sides of each transaction
    public const string InterfaceToken = "com.vpnhood.core.vpnservice.IMessageTransport";

    // client -> service, oneway: [requestId:int, replyBinder:IBinder, request:byte[]] (IBinder.FIRST_CALL_TRANSACTION)
    public const int RequestTransactionCode = 1;

    // service -> client reply binder, confirmed: [requestId:int, success:int, response:byte[] | errorMessage:string]
    public const int ReplyTransactionCode = 2;

    // writes a blob without cloning when the memory is array-backed (the parcel copies it anyway)
    public static void WriteBlob(Parcel data, Memory<byte> blob)
    {
        if (MemoryMarshal.TryGetArray<byte>(blob, out var segment))
            data.WriteByteArray(segment.Array, segment.Offset, segment.Count);
        else
            data.WriteByteArray(blob.ToArray());
    }
}
