using VpnHood.Core.Toolkit.Graphics;

namespace VpnHood.Core.Client.VpnServices.Host;

public sealed class VpnServiceLocalization
{
    public string Disconnect { get; set; } = "Disconnect";
    public string Manage { get; set; } = "Manage";
    public string? WindowBackgroundColor { get; set; }

    public static VhColor? TryParseColorFromHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return null;

        try {
            hex = hex.Replace("#", ""); // Remove the hash if present
            if (hex.Length == 6)
                return VhColor.FromArgb(255, // Default Alpha
                    Convert.ToByte(hex[..2], 16),
                    Convert.ToByte(hex[2..4], 16),
                    Convert.ToByte(hex[4..6], 16));

            if (hex.Length == 8)
                return VhColor.FromArgb(
                    Convert.ToByte(hex[..2], 16),
                    Convert.ToByte(hex[2..4], 16),
                    Convert.ToByte(hex[4..6], 16),
                    Convert.ToByte(hex[6..8], 16));

            return null;
        }
        catch {
            return null;
        }
    }
}