using VpnHood.Core.Client.Abstractions;
using VpnHood.Net.Toolkit.Graphics;

namespace VpnHood.Core.Client.VpnServices.Abstractions;

/// <summary>
/// Represents the words the VPN service shows itself, as its notification's, in the app's language, and the color it
/// shows them on.
/// </summary>
public sealed class VpnServiceLocalization
{
    /// <summary>Gets or sets the word of the notification's disconnect button. The default is "Disconnect".</summary>
    public string Disconnect { get; set; } = "Disconnect";

    /// <summary>
    /// Gets or sets the word of the notification's button that opens the app. The default is "Manage".
    /// </summary>
    public string Manage { get; set; } = "Manage";

    /// <summary>
    /// Gets or sets the connection's states in the app's words, as its main page says them; a state without one is said
    /// in English. The default is none.
    /// </summary>
    public IReadOnlyDictionary<ClientState, string> ClientStates { get; set; } = new Dictionary<ClientState, string>();

    /// <summary>
    /// Gets or sets the app's window color, as hex that <see cref="TryParseColorFromHex"/> reads, which the
    /// notification takes on Android, or <see langword="null"/>.
    /// </summary>
    public string? WindowBackgroundColor { get; set; }

    /// <summary>Gets a state's text: the app's word for it, or else English.</summary>
    /// <param name="clientState">The state.</param>
    /// <returns>The state's text.</returns>
    public string GetClientStateText(ClientState clientState)
    {
        return ClientStates.GetValueOrDefault(clientState) ?? clientState switch {
            ClientState.Initializing => "Initializing",
            ClientState.ValidatingProxies => "Validating proxies",
            ClientState.FindingReachableServer => "Finding network",
            ClientState.FindingBestServer => "Finding best server",
            ClientState.Connecting => "Connecting",
            ClientState.Connected => "Connected",
            ClientState.Unstable => "Unstable",
            ClientState.Waiting => "Waiting",
            ClientState.WaitingForAd => "Loading Ad",
            ClientState.Disconnecting => "Disconnecting",
            _ => "Disconnected"
        };
    }

    /// <summary>
    /// Reads a color written as hex: <c>RRGGBB</c>, which is opaque, or <c>AARRGGBB</c>, with or without <c>#</c>.
    /// </summary>
    /// <param name="hex">The text.</param>
    /// <returns>The color, or <see langword="null"/> where the text is empty or no such color.</returns>
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