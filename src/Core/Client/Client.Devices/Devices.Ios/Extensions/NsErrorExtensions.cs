namespace VpnHood.Core.Client.Devices.Ios.Extensions;

// iOS counterpart of AndroidUtils.JavaExceptionToApiException: turns a native NSError into a managed
// exception. Apple puts the actionable reason (an underlying framework code, a server message) in
// userInfo and the NSUnderlyingError chain, never in Code/LocalizedDescription, so the whole chain is
// flattened into the message while the NSError itself stays reachable via the inner NSErrorException.
public static class NsErrorExtensions
{
    private const string UnderlyingErrorKey = "NSUnderlyingError";

    // the NSUnderlyingError chain can be self-referencing, and only the first few links carry
    // anything a human can act on.
    private const int MaxChainDepth = 4;

    // Apple's two generic "the user backed out" errors; frameworks map their own cancel onto one of
    // them, so a cancel can be told apart from a real failure without knowing the framework.
    private const string UrlErrorDomain = "NSURLErrorDomain";
    private const int UrlErrorCancelled = -999;
    private const string CocoaErrorDomain = "NSCocoaErrorDomain";
    private const int UserCancelledError = 3072;

    extension(NSError error)
    {
        /// <summary>
        /// Flattens the error with its userInfo and its NSUnderlyingError chain into a single line.
        /// </summary>
        public string Describe()
        {
            var parts = new List<string>();
            var current = error;
            for (var depth = 0; current != null && depth < MaxChainDepth; depth++) {
                parts.Add($"[{current.Domain} {current.Code}] {current.LocalizedDescription}");

                // UserInfo is declared non-nullable by the binding but is null for errors built
                // without one.
                var userInfo = current.UserInfo;
                foreach (var key in userInfo.Keys) {
                    var name = key.ToString();
                    if (name != UnderlyingErrorKey)
                        parts.Add($"{name}={userInfo.ObjectForKey(key)}");
                }

                current = userInfo.ObjectForKey(new NSString(UnderlyingErrorKey)) as NSError;
            }

            return string.Join(" | ", parts);
        }

        /// <summary>
        /// True when the error is one of Apple's generic user-cancelled errors. A framework-specific
        /// cancel (ASAuthorizationError.Canceled, …) is NOT covered; its caller must check its own code.
        /// </summary>
        public bool IsUserCancelled => error.Domain switch {
            UrlErrorDomain => error.Code == UrlErrorCancelled,
            CocoaErrorDomain => error.Code == UserCancelledError,
            _ => false
        };

        /// <summary>
        /// Converts the error into the managed exception iOS itself throws, so the NSError stays
        /// reachable through NSErrorException.Error. Its Message is LocalizedDescription alone, so a
        /// caller that wants the actionable reason logs or wraps <see cref="Describe"/> next to it.
        /// A cancel is NOT turned into an OperationCanceledException: in this codebase a cancellation
        /// is decided by the caller's token, never by the exception type, and an OperationCanceled
        /// thrown with no token cancelled is swallowed as a benign abort by the layers above.
        /// </summary>
        public NSErrorException ToException()
        {
            return new NSErrorException(error);
        }
    }
}
