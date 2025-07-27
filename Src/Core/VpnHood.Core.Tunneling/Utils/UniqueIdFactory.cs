namespace VpnHood.Core.Tunneling.Utils;

public static class UniqueIdFactory
{
    public static int DebugInitId { get; set; } = 1000;

#if DEBUG
    private static int _lastId;

    public static string Create()
    {
        if (_lastId==0)
            _lastId = DebugInitId;

        // return an id in this format: "ReqId-0001"
        var id = Interlocked.Increment(ref _lastId);
        return $"ReqId-{id:D4}";
    }

#else
    public static string Create() => Guid.NewGuid().ToString();
#endif

}