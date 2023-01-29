using Microsoft.Extensions.Logging;

namespace VpnHood.AccessServer;

public static class AccessEventId
{
    public static EventId Server { get; } = new((int)EventCode.Server, nameof(Server));
    public static EventId Session { get; } = new((int)EventCode.Session, nameof(Session));
    public static EventId AddUsage { get; } = new((int)EventCode.AddUsage, nameof(AddUsage));
    public static EventId Cache { get; } = new((int)EventCode.Cache, nameof(Cache));
    public static EventId Archive { get; } = new((int)EventCode.Archive, nameof(Archive));
    public static EventId Maintenance { get; } = new((int)EventCode.Maintenance, nameof(Maintenance));

    private enum EventCode
    {
        Server,
        AddUsage,
        Session,
        Cache,
        Archive,
        Maintenance,
    }

}