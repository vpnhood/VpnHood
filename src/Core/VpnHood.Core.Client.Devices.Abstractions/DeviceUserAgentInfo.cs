namespace VpnHood.Core.Client.Devices.Abstractions;

// What a browser on this device sends as its user agent, in the parts the app builds its own from: the
// platform in the parentheses ("Linux; Android 14"), the model a browser adds there where one does
// (Android's), and the browser after them, which says "Mobile" on a phone. Google Analytics parses the
// OS and the device from it.
public class DeviceUserAgentInfo
{
    public required string Platform { get; init; }
    public string? Model { get; init; }
    public required string Browser { get; init; }

    // Chrome's words after the platform, of one recent version for every device: Google Analytics wants
    // a browser around the platform, and nobody reads its version
    public static string GetChromeBrowser(bool isMobile)
    {
        return $"AppleWebKit/537.36 (KHTML, like Gecko) Chrome/150.0.0.0 {(isMobile ? "Mobile " : "")}Safari/537.36";
    }
}
