using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Core.Client.VpnServices.Abstractions.Tracking;

/// <summary>
/// Provides the passage of a tracker factory to the VPN engine, by its kind (<see cref="TrackerKinds"/>): GA4's as its
/// measurement ID, any other as the .NET type the engine makes again in its own process.
/// </summary>
public static class TrackerFactorySerializer
{
    /// <summary>Writes a factory as its kind and settings.</summary>
    /// <param name="trackerFactory">The factory.</param>
    /// <returns>
    /// <see cref="TrackerKinds.Ga4"/> with the measurement ID for a <see cref="Ga4TrackerFactory"/>; otherwise,
    /// <see cref="TrackerKinds.DotNet"/> with the factory's assembly-qualified type name and its public properties.
    /// </returns>
    /// <exception cref="InvalidOperationException">The factory's type has no assembly-qualified name.</exception>
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

    /// <summary>Makes a factory again from its kind and settings.</summary>
    /// <param name="trackerFactoryInfo">The factory's kind and settings.</param>
    /// <returns>
    /// The factory, or <see langword="null"/> where this engine cannot make it, which it logs: a kind it does not have,
    /// a type this process lacks, or settings that do not fit.
    /// </returns>
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
