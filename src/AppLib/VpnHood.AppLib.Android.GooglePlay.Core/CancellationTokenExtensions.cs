using Android.OS;

namespace VpnHood.AppLib.Droid.GooglePlay;

public static class CancellationTokenExtensions
{
    // do not dispose this CancellationSignal, because it will be cancelled by system when cancellationToken is cancelled,
    // and we don't want to dispose it before that. It may cause system crash if we dispose it too early.
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