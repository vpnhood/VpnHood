namespace VpnHood.Net.Toolkit.Exceptions;

/// <summary>
/// The exception that is thrown when an object to add already exists. Its <see cref="Exception.Data"/> carries
/// <c>HttpStatusCode</c> 409, for an API that maps it to its response.
/// </summary>
public sealed class AlreadyExistsException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="AlreadyExistsException"/> class.</summary>
    /// <param name="collectionName">The name of the collection the object exists in.</param>
    public AlreadyExistsException(string collectionName) :
        base($"Object already exists in {collectionName}.")
    {
        CollectionName = collectionName;
        Data["HttpStatusCode"] = 409;
    }

    /// <summary>Initializes a new instance of the <see cref="AlreadyExistsException"/> class.</summary>
    /// <param name="collectionName">The name of the collection the object exists in.</param>
    /// <param name="innerException">The exception that is the cause of this one.</param>
    public AlreadyExistsException(string collectionName, Exception innerException) :
        base($"Object already exists in {collectionName}.", innerException)
    {
        CollectionName = collectionName;
        Data["HttpStatusCode"] = 409;
    }

    /// <summary>Gets the name of the collection the object exists in.</summary>
    public string CollectionName { get; }

    /// <summary>
    /// Determines, by a heuristic, whether an exception, or one of its inner exceptions, tells that an object already
    /// exists: an <see cref="AlreadyExistsException"/>, a SQL Server error 2601 or 2627 in its data, or SQLite's
    /// "UNIQUE constraint failed" in its message.
    /// </summary>
    /// <param name="ex">The exception.</param>
    /// <returns><see langword="true"/> if it tells so; otherwise, <see langword="false"/>.</returns>
    public static bool Is(Exception ex)
    {
        if (ex is AlreadyExistsException)
            return true;

        // SQL Server: unique index / unique constraint violation
        if (ex.Data.Contains("HelpLink.EvtID") && ex.Data["HelpLink.EvtID"]?.ToString() is "2601" or "2627")
            return true;

        // SQLite: unique/primary-key violation (error 19)
        if (ex.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase))
            return true;

        return ex.InnerException != null && Is(ex.InnerException);
    }
}