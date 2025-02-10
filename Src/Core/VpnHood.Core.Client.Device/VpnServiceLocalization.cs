using System.Drawing;

namespace VpnHood.Core.Client.Device;

public sealed class VpnServiceLocalization
{
    public string Disconnect { get; set; } = "Disconnect";
    public string Manage { get; set; } = "Manage";
    public string? WindowBackgroundColor { get; set; }

    public static Color? TryParseColorFromHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return null;

        try {
            hex = hex.Replace("#", ""); // Remove the hash if present
            if (hex.Length == 6)
                return Color.FromArgb(255, // Default Alpha
                    Convert.ToInt32(hex[..2], 16),
                    Convert.ToInt32(hex[2..4], 16),
                    Convert.ToInt32(hex[4..6], 16));

            if (hex.Length == 8)
                return Color.FromArgb(
                    Convert.ToInt32(hex[..2], 16),
                    Convert.ToInt32(hex[2..4], 16),
                    Convert.ToInt32(hex[4..6], 16),
                    Convert.ToInt32(hex[6..8], 16));

            return null;
        }
        catch {
            return null;

        }
    }
}