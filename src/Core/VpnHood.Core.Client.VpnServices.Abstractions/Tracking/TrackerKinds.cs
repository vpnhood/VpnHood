namespace VpnHood.Core.Client.VpnServices.Abstractions.Tracking;

/// <summary>
/// Provides the kinds of tracker an engine may make (<see cref="VpnHood.Core.Client.Abstractions.TrackerFactoryInfo.Kind"/>),
/// each with its settings' shape; an engine reports the kinds it has (<see cref="VpnEngineFeatures.TrackerKinds"/>).
/// </summary>
public static class TrackerKinds
{
    /// <summary>
    /// GA4's own protocol, which an engine in any language may speak: <c>ga4</c>, with the settings
    /// <c>{ "MeasurementId": "G-..." }</c>, where no ID sends nothing.
    /// </summary>
    public const string Ga4 = "ga4";

    /// <summary>
    /// A .NET factory that a .NET engine makes from its type and its public properties: <c>dotnet</c>, with the settings
    /// <c>{ "Type": "&lt;assembly-qualified name&gt;", "Settings": { ... } }</c>; an engine in another language has
    /// none.
    /// </summary>
    public const string DotNet = "dotnet";
}
