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
    private static readonly object InitLock = new();
    private static QUIC_API_TABLE* _table;
    private static QUIC_HANDLE* _registration;
    private static bool _initialized;
    private static bool _supported;

    public static QUIC_API_TABLE* Table {
        get {
            EnsureInitialized();
            return _table;
        }
    }

    public static QUIC_HANDLE* Registration {
        get {
            EnsureInitialized();
            return _registration;
        }
    }

    /// <summary>True if libmsquic.so loaded and the API/registration opened successfully.</summary>
    public static bool IsSupported {
        get {
            try {
                EnsureInitialized();
                return _supported;
            }
            catch {
                return false;
            }
        }
    }

    private static void EnsureInitialized()
    {
        if (_initialized) {
            if (!_supported)
                throw new NotSupportedException("MsQuic (libmsquic.so) is not available on this device.");
            return;
        }

        lock (InitLock) {
            if (!_initialized) {
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
                    _table = table;
                    _registration = reg;
                    _supported = true;
                }
                catch {
                    _supported = false;
                }
                finally {
                    _initialized = true;
                }
            }
        }

        if (!_supported)
            throw new NotSupportedException("MsQuic (libmsquic.so) is not available on this device.");
    }
}
