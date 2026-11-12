namespace VpnHood.AppLib.App.Ios;

// The init params, and the two facts iOS makes a head own: the App Group its app and its Network
// Extension share - where their IPC folder lives - and the extension's bundle id, which the VPN
// configuration names as its provider. From them the platform builds its own device.
/// <summary>
/// Represents the init params of the app on iOS: those of every platform, and the two facts iOS makes the head own.
/// </summary>
public class IosInitParams : AppInitParams
{
    /// <summary>Gets the App Group the app and its Network Extension share, where the VPN service's folder lives.</summary>
    public required string AppGroupId { get; init; }
    /// <summary>Gets the Network Extension's bundle ID, which the VPN configuration names as its provider.</summary>
    public required string ProviderBundleId { get; init; }
}
