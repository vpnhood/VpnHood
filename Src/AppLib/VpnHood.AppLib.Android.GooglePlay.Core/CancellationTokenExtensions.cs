using Android.OS;

namespace VpnHood.AppLib.Droid.GooglePlay;

public static class CancellationTokenExtensions
{
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