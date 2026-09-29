using System.Text.Json.Serialization;
using VpnHood.AppLib.Api.App;

namespace VpnHood.AppUi.Hosting.Cli.Channel;

// What the service tells a caller over the channel: its API's address, token and all, which moves at
// each rebind; or, to anyone but an administrator, the refusal alone.
public class DaemonChannelAnswer
{
    public Uri? ApiUrl { get; init; }
    public int ProcessId { get; init; }
    public required string Version { get; init; }

    // The sentence for a caller who may not use the app; null for one who may.
    public string? Refusal { get; init; }

    [JsonIgnore]
    public string? Token => ApiUrl == null ? null : LocalApiToken.Read(ApiUrl);

    // The version this binary is, which both sides send.
    public static string CurrentVersion { get; } =
        typeof(DaemonChannelAnswer).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
