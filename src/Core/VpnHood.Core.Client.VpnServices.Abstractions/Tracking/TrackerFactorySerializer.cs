using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Core.Client.VpnServices.Abstractions.Tracking;

// A tracker factory on its way to the VPN service, which makes it again in its own process from its
// type and its public properties.
public static class TrackerFactorySerializer
{
    public static TrackerFactoryInfo Serialize(ITrackerFactory trackerFactory)
    {
        var type = trackerFactory.GetType();
        return new TrackerFactoryInfo {
            AssemblyQualifiedName = type.AssemblyQualifiedName ??
                                    throw new InvalidOperationException($"{type.Name} has no type name."),
            Settings = JsonSerializer.SerializeToElement(trackerFactory, type)
        };
    }

    // null when this process cannot make it, which it logs: a type it does not have, or settings that
    // do not fit it
    public static ITrackerFactory? TryDeserialize(TrackerFactoryInfo trackerFactoryInfo)
    {
        try {
            var type = Type.GetType(trackerFactoryInfo.AssemblyQualifiedName) ??
                       throw new InvalidOperationException("This process has no such type.");

            return trackerFactoryInfo.Settings.Deserialize(type) as ITrackerFactory ??
                   throw new InvalidOperationException("The type is no tracker factory.");
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not make the tracker factory. Type: {Type}",
                trackerFactoryInfo.AssemblyQualifiedName);
            return null;
        }
    }
}
