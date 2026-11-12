using System.Runtime.InteropServices;
using Android.OS;

namespace VpnHood.Core.Client.Devices.Android.Messaging;

/// <summary>
/// Provides the contract of the binder channel between the app and its Android VPN service, which both ends use: the
/// app's client (<c>AndroidMessageClient</c>) and the service's listener (<c>AndroidMessageListener</c>).
/// </summary>
/// <remarks>
/// It carries the same request and answer bytes as the other channels. Every transaction is oneway: a request carries
/// its ID, the client's reply binder and its bytes, and the service answers on the reply binder. Nothing may go to the
/// app synchronously: Android kills a frozen process that receives a synchronous transaction, and the app may be
/// frozen, as a cached process, while it waits for a reply, while a oneway transaction waits until it thaws. No thread
/// on either side blocks while a request runs.
/// </remarks>
public static class AndroidMessageTransport
{
    private const int DefaultMaxIpcSize = 64 * 1024;
    private const int ParcelOverheadAllowance = 4 * 1024;

    /// <summary>The longest error message a failed answer carries: 4,096 characters.</summary>
    public const int MaxErrorMessageLength = 4 * 1024;

    /// <summary>
    /// The longest request or answer, in bytes: the IPC size Android suggests, 64 KB before Android 11, less 4 KB for the
    /// interface token, the request's own data and Binder's bookkeeping.
    /// </summary>
    public static readonly int MaxBlobLength =
        (OperatingSystem.IsAndroidVersionAtLeast(30)
            ? IBinder.SuggestedMaxIpcSizeBytes
            : DefaultMaxIpcSize) - ParcelOverheadAllowance;

    /// <summary>
    /// The action of the bind that selects the channel; any other, as the system's <c>android.net.VpnService</c>, must
    /// go on to <c>VpnService.OnBind</c>.
    /// </summary>
    public const string BindAction = "com.vpnhood.core.vpnservice.MESSAGE";

    /// <summary>The interface token each transaction's parcel carries, which both sides check.</summary>
    public const string InterfaceToken = "com.vpnhood.core.vpnservice.IMessageTransport";

    /// <summary>
    /// The code of a request, client to service, oneway: its ID, the reply binder and its bytes
    /// (<c>IBinder.FIRST_CALL_TRANSACTION</c>).
    /// </summary>
    public const int RequestTransactionCode = 1;

    /// <summary>
    /// The code of an answer, service to the client's reply binder, oneway: the request's ID, whether it succeeded, and
    /// its bytes or its error message.
    /// </summary>
    public const int ReplyTransactionCode = 2;

    /// <summary>Writes bytes to a parcel, without a copy of its own where the memory is an array's.</summary>
    /// <param name="data">The parcel.</param>
    /// <param name="blob">The bytes.</param>
    public static void WriteBlob(Parcel data, Memory<byte> blob)
    {
        if (MemoryMarshal.TryGetArray<byte>(blob, out var segment))
            data.WriteByteArray(segment.Array, segment.Offset, segment.Count);
        else
            data.WriteByteArray(blob.ToArray());
    }
}
