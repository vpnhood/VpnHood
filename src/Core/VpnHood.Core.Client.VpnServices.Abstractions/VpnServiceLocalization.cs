using VpnHood.Core.Client.Abstractions;
using VpnHood.Net.Toolkit.Graphics;

namespace VpnHood.Core.Client.VpnServices.Abstractions;

public sealed class VpnServiceLocalization
{
    public string Disconnect { get; set; } = "Disconnect";
    public string Manage { get; set; } = "Manage";

    // The connection's states in the app's words, as its main page says them; a state without one is
    // said in English.
    public IReadOnlyDictionary<ClientState, string> ClientStates { get; set; } = new Dictionary<ClientState, string>();
    public string? WindowBackgroundColor { get; set; }

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