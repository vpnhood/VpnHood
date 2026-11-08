namespace VpnHood.Net.Toolkit.Exceptions;

/// <summary>
/// The exception that is thrown when an object is not found. Its <see cref="Exception.Data"/> carries
/// <c>HttpStatusCode</c> 404, for an API that maps it to its response.
/// </summary>
public sealed class NotExistsException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="NotExistsException"/> class.</summary>
    /// <param name="message">The message, or <see langword="null"/> for the default one.</param>
    public NotExistsException(string? message = null)
        : base(message)
    {
        Data["HttpStatusCode"] = 404;
    }

    /// <summary>Initializes a new instance of the <see cref="NotExistsException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The exception that is the cause of this one.</param>
    public NotExistsException(string message, Exception innerException)
        : base(message, innerException)
    {
        Data["HttpStatusCode"] = 404;
    }

    /// <summary>
    /// Determines, by a heuristic, whether an exception, or one of its inner exceptions, tells that an object was not
    /// found: a <see cref="NotExistsException"/>, a <see cref="KeyNotFoundException"/>, or the English messages of an
    /// empty LINQ or EF Core query, or of an EF Core change that affected no row.
    /// </summary>
    /// <param name="ex">The exception.</param>
    /// <returns><see langword="true"/> if it tells so; otherwise, <see langword="false"/>.</returns>
    public static bool Is(Exception ex)
    {
        if (ex is NotExistsException or KeyNotFoundException)
            return true;

        // linq and ef core select
        if (ex is InvalidOperationException && (
                ex.Message.Contains("Sequence contains no matching element") ||
                ex.Message.Contains("Sequence contains no elements")))
            return true;

        // ef core remove
        if (ex.Message.Contains("The database operation was expected to affect") &&
            ex.Message.Contains("but actually affected 0 row"))
            return true;

        return ex.InnerException != null && Is(ex.InnerException);
    }
}