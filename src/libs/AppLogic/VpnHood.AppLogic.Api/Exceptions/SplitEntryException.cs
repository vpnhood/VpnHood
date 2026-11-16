namespace VpnHood.AppLogic.Api.Exceptions;

/// <summary>
/// Represents an entry that a split tunneling list refuses when it is saved; its data's <c>Reason</c> names a
/// <see cref="SplitEntryError"/> and its <c>Entry</c> the entry, as the UI words them.
/// </summary>
public class SplitEntryException : FormatException
{
    private SplitEntryException(string message, Exception? innerException) : base(message, innerException)
    {
    }

    /// <summary>Creates the error of an entry that a split tunneling list refuses.</summary>
    /// <param name="reason">Why.</param>
    /// <param name="entry">The entry, as it was given.</param>
    /// <param name="innerException">The parser's own error, or <see langword="null"/>.</param>
    /// <returns>The error.</returns>
    public static SplitEntryException Create(SplitEntryError reason, string entry, Exception? innerException = null)
    {
        var message = reason switch {
            SplitEntryError.Asterisk =>
                $"Asterisk (*) is not supported; remove it. A domain already covers all its subdomains: {entry}",
            _ => $"This is not an IP address, range or network: {entry}"
        };

        // ReSharper disable once UseObjectOrCollectionInitializer
        var exception = new SplitEntryException(message, innerException);
        exception.Data["Reason"] = reason.ToString();
        exception.Data["Entry"] = entry;
        return exception;
    }
}
