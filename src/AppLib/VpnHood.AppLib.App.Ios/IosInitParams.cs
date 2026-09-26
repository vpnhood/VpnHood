namespace VpnHood.AppLib.App.Ios;

// The init params, and the two facts iOS makes a head own: the App Group its app and its Network
// Extension share - where their IPC folder lives - and the extension's bundle id, which the VPN
// configuration names as its provider. From them the platform builds its own device.
public class IosInitParams : AppInitParams
{
    public required string AppGroupId { get; init; }
    public required string ProviderBundleId { get; init; }
}
