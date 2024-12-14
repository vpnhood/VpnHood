namespace VpnHood.Core.Common.Exceptions;

public sealed class AlreadyExistsException : Exception
{
    public AlreadyExistsException(string collectionName) :
        base($"Object already exists in {collectionName}.")
    {
        CollectionName = collectionName;
        Data["HttpStatusCode"] = 409;
    }

    public string CollectionName { get; }

    public static bool Is(Exception ex)
    {
        if (ex is AlreadyExistsException)
            return true;

        if (ex.Data.Contains("HelpLink.EvtID") && ex.Data["HelpLink.EvtID"]?.ToString() is "2601" or "2627")
            return true;

        return ex.InnerException != null && Is(ex.InnerException);
    }
}