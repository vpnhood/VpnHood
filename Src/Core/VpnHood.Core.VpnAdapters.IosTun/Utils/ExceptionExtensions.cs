using Foundation;

namespace VpnHood.Core.VpnAdapters.IosTun.Utils;

public static class ExceptionExtensions
{
    extension(Exception exception)
    {
        // Wraps a managed Exception in an NSError using the built-in NSExceptionError subclass,
        // preserving the original exception type and message (accessible via NSExceptionError.Exception).
        public NSExceptionError ToNsExceptionError()
            => new(exception);
    }

}
