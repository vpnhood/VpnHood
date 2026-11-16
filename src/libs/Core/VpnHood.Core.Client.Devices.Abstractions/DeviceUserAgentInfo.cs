namespace VpnHood.Core.Client.Devices.Abstractions;

/// <summary>
/// Represents what a browser on the device sends as its user agent, in the parts the app builds its own from.
/// </summary>
/// <remarks>Google Analytics reads the OS and the device from it.</remarks>
public class DeviceUserAgentInfo
{
    /// <summary>Gets the platform, which goes in the parentheses, as <c>Linux; Android 14</c>.</summary>
    public required string Platform { get; init; }

    /// <summary>Gets the device's model, which a browser adds in the parentheses where one does, or <see langword="null"/>.</summary>
    public string? Model { get; init; }

    /// <summary>Gets the browser's words after the parentheses, which say <c>Mobile</c> on a phone.</summary>
    public required string Browser { get; init; }

    /// <summary>
    /// Gets Chrome's words after the platform, of one fixed version, Chrome 150, for every device.
    /// </summary>
    /// <param name="isMobile"><see langword="true"/> for a phone's, with <c>Mobile</c>.</param>
    /// <returns>The words, as <c>AppleWebKit/537.36 (KHTML, like Gecko) Chrome/150.0.0.0 Safari/537.36</c>.</returns>
    // Google Analytics wants a browser around the platform, and nobody reads its version
    public static string GetChromeBrowser(bool isMobile)
    {
        return $"AppleWebKit/537.36 (KHTML, like Gecko) Chrome/150.0.0.0 {(isMobile ? "Mobile " : "")}Safari/537.36";
    }
}
