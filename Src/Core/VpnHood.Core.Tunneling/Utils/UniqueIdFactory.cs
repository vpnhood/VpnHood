namespace VpnHood.Core.Tunneling.Utils;

public static class UniqueIdFactory
{
#if DEBUG
    private static int _lastId;

    public static string Create()
    {
        // return an id in this format: "ReqId-0001"
        var id = Interlocked.Increment(ref _lastId);
        return $"ReqId-{id:D4}";
    }

#else
    public static string Create() => Guid.NewGuid().ToString();
#endif

}