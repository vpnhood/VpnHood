namespace VpnHood.AppLib.Contracts.Device;

// An app installed on this device, as the split-tunneling picker shows it. The app's own shape,
// not the device layer's: what a UI needs is a name, an id and an icon, and the engine's device
// abstraction is free to learn more without that reaching a picker.
public class DeviceAppInfo
{
    public required string AppId { get; init; }
    public required string AppName { get; init; }
    public required string IconPng { get; init; }
}
