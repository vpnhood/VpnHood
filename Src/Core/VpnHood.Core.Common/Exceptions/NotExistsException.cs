namespace VpnHood.Core.Common.Exceptions;

public sealed class NotExistsException : Exception
{
    public NotExistsException(string? message = null)
        : base(message)
    {
        Data["HttpStatusCode"] = 404;
    }

    public NotExistsException(string message, Exception innerException)
        : base(message, innerException)
    {
        Data["HttpStatusCode"] = 404;
    }


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