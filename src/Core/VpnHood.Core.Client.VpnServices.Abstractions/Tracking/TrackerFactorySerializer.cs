using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Core.Client.VpnServices.Abstractions.Tracking;

// A tracker factory on its way to the VPN engine, by its kind (TrackerKinds): GA4's as its measurement id,
// any other as the .NET type the engine makes again in its own process.
public static class TrackerFactorySerializer
{
    public static TrackerFactoryInfo Serialize(ITrackerFactory trackerFactory)
    {
        if (trackerFactory is Ga4TrackerFactory ga4TrackerFactory)
            return new TrackerFactoryInfo {
                Kind = TrackerKinds.Ga4,
                Settings = JsonSerializer.SerializeToElement(ga4TrackerFactory)
            };

        var type = trackerFactory.GetType();
        return new TrackerFactoryInfo {
            Kind = TrackerKinds.DotNet,
            Settings = JsonSerializer.SerializeToElement(new DotNetTrackerSettings {
                Type = type.AssemblyQualifiedName ??
                       throw new InvalidOperationException($"{type.Name} has no type name."),
                Settings = JsonSerializer.SerializeToElement(trackerFactory, type)
            })
        };
    }

    // null when this engine cannot make it, which it logs: a kind it does not have, a type this process
    // lacks, or settings that do not fit
    public static ITrackerFactory? TryDeserialize(TrackerFactoryInfo trackerFactoryInfo)
    {
        try {
            return trackerFactoryInfo.Kind switch {
                TrackerKinds.Ga4 => trackerFactoryInfo.Settings.Deserialize<Ga4TrackerFactory>() ??
                                    throw new InvalidOperationException("The settings are empty."),
                TrackerKinds.DotNet => DeserializeDotNet(trackerFactoryInfo.Settings),
                _ => throw new NotSupportedException("This engine has no tracker of this kind.")
            };
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not make the tracker factory. Kind: {Kind}", trackerFactoryInfo.Kind);
            return null;
        }
    }

    private static ITrackerFactory DeserializeDotNet(JsonElement settings)
    {
        var dotNetSettings = settings.Deserialize<DotNetTrackerSettings>() ??
                             throw new InvalidOperationException("The settings are empty.");
        var type = Type.GetType(dotNetSettings.Type) ??
                   throw new InvalidOperationException($"This process has no such type. Type: {dotNetSettings.Type}");
        return dotNetSettings.Settings.Deserialize(type) as ITrackerFactory ??
               throw new InvalidOperationException($"The type is no tracker factory. Type: {dotNetSettings.Type}");
    }

    private sealed class DotNetTrackerSettings
    {
        public required string Type { get; init; }
        public required JsonElement Settings { get; init; }
    }
}
