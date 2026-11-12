using Android.OS;

namespace VpnHood.AppLib.Stores.GooglePlay;

/// <summary>Provides the conversion of a cancellation token to Android's cancellation signal.</summary>
public static class CancellationTokenExtensions
{
    /// <summary>Creates an Android cancellation signal that is canceled with the token.</summary>
    /// <param name="cancellationToken">The token.</param>
    /// <returns>The signal, canceled already where the token is.</returns>
    /// <remarks>
    /// The caller must not dispose the signal: the token's cancel cancels it, and an early disposal can crash the
    /// process.
    /// </remarks>
    public static CancellationSignal ToCancellationSignal(this CancellationToken cancellationToken)
    {
        var cancellationSignal = new CancellationSignal();

        // if already cancelled, cancel immediately
        if (cancellationToken.IsCancellationRequested) {
            cancellationSignal.Cancel();
            return cancellationSignal;
        }

        // register to cancel when the token is cancelled
        if (cancellationToken.CanBeCanceled)
            cancellationToken.Register(() => {
                if (!cancellationSignal.IsCanceled) {
                    try {
                        cancellationSignal.Cancel();
                    }
                    catch {
                        // make sure no exception is thrown
                    }
                }
            });

        return cancellationSignal;
    }
}