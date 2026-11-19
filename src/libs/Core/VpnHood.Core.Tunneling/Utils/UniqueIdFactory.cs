namespace VpnHood.Core.Tunneling.Utils;

/// <summary>Provides unique IDs, as requests and connections carry in their logs.</summary>
public static class UniqueIdFactory
{
    /// <summary>
    /// Gets or sets the number a debug build's IDs count up from, before the first ID. The default is 1000.
    /// </summary>
    public static int DebugInitId { get; set; } = 1000;

#if DEBUG
    private static int _lastId;
    private static bool _isSeeded;
    private static readonly Lock SeedLock = new();

    /// <summary>
    /// Creates a unique ID: a GUID, or, in a debug build, a counted one such as <c>ReqId-1001</c>, which its logs make
    /// easier to follow.
    /// </summary>
    /// <returns>The ID.</returns>
    public static string Create()
    {
        // the count starts at the seed once, before any ID, even for two first calls at once
        if (!Volatile.Read(ref _isSeeded)) {
            lock (SeedLock) {
                if (!_isSeeded) {
                    _lastId = DebugInitId;
                    Volatile.Write(ref _isSeeded, true);
                }
            }
        }

        // return an id in this format: "ReqId-0001"
        var id = Interlocked.Increment(ref _lastId);
        return $"ReqId-{id:D4}";
    }

#else
    /// <summary>
    /// Creates a unique ID: a GUID, or, in a debug build, a counted one such as <c>ReqId-1001</c>, which its logs make
    /// easier to follow.
    /// </summary>
    /// <returns>The ID.</returns>
    public static string Create() => Guid.NewGuid().ToString();
#endif
}