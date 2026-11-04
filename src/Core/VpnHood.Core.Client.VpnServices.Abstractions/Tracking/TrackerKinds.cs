namespace VpnHood.Core.Client.VpnServices.Abstractions.Tracking;

// The kinds of tracker an engine may make (TrackerFactoryInfo.Kind), each with its settings' shape. An engine
// reports the kinds it has (VpnEngineFeatures.TrackerKinds).
public static class TrackerKinds
{
    // GA4's own protocol, which an engine in any language may speak: { "MeasurementId": "G-..." }, where no id
    // sends nothing
    public const string Ga4 = "ga4";

    // a .NET factory that a .NET engine makes from its type and its public properties:
    // { "Type": "<assembly-qualified name>", "Settings": { ... } }; an engine in another language has none
    public const string DotNet = "dotnet";
}
