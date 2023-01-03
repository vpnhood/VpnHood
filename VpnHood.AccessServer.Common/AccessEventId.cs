using Microsoft.Extensions.Logging;

namespace VpnHood.AccessServer;

public static class AccessEventId
{
    public static EventId Session = new((int)EventCode.Session, nameof(Session));
    public static EventId AddUsage = new((int)EventCode.AddUsage, nameof(AddUsage));
    public static EventId Cache = new((int)EventCode.Cache, nameof(Cache));
    public static EventId Archive = new((int)EventCode.Archive, nameof(Archive));

    private enum EventCode
    {
        AddUsage = 100,
        Session,
        Cache,
        Archive,
    }

}