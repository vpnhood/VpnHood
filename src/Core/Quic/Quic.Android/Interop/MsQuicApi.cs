using System.Runtime.InteropServices;
using Microsoft.Quic;
using static Microsoft.Quic.MsQuic;

namespace VpnHood.Core.Quic.Droid.Interop;

/// <summary>
/// Process-wide MsQuic library handle: opens the native QUIC API table and a single registration
/// (lazily, once) on top of the bundled libmsquic.so. This is the Android equivalent of what
/// System.Net.Quic does internally — but driven by our own P/Invoke bindings so it does not depend
/// on the OpenSSL crypto shim that .NET-Android lacks.
/// </summary>
internal static unsafe class MsQuicApi
{
    // The native handles, opened at most once. Kept in a single immutable object published atomically by
    // Lazy<T> (ExecutionAndPublication), so there is no hand-rolled double-checked locking — i.e. no field
    // read both inside and outside a lock.
    private sealed class Handles(QUIC_API_TABLE* apiTable, QUIC_HANDLE* registrationHandle)
    {
        public readonly QUIC_API_TABLE* ApiTable = apiTable;
        public readonly QUIC_HANDLE* RegistrationHandle = registrationHandle;
    }

    private static readonly Lazy<Handles?> LazyHandles = new(TryOpen);

    public static QUIC_API_TABLE* Table => GetHandles().ApiTable;

    public static QUIC_HANDLE* Registration => GetHandles().RegistrationHandle;

    /// <summary>True if libmsquic.so loaded and the API/registration opened successfully.</summary>
    public static bool IsSupported => LazyHandles.Value != null;

    private static Handles GetHandles() =>
        LazyHandles.Value ?? throw new NotSupportedException("MsQuic (libmsquic.so) is not available on this device.");

    // Returns null (instead of throwing) when msquic is unavailable, so IsSupported reports false and the
    // client falls back to TCP. Runs at most once.
    private static Handles? TryOpen()
    {
        try {
            var table = Open();
            // AppName must outlive RegistrationOpen; intentionally leaked (process lifetime).
            var appName = (sbyte*)Marshal.StringToCoTaskMemUTF8("VpnHood");
            var regConfig = new QUIC_REGISTRATION_CONFIG {
                AppName = appName,
                ExecutionProfile = QUIC_EXECUTION_PROFILE.LOW_LATENCY
            };
            QUIC_HANDLE* reg;
            ThrowIfFailure(table->RegistrationOpen(&regConfig, &reg));
            return new Handles(table, reg);
        }
        catch {
            return null;
        }
    }
}
