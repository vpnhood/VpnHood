using System.Text.Json.Serialization;
using VpnHood.AppLib.Api.App;

namespace VpnHood.AppUi.Hosting.Desktop.Channel;

/// <summary>
/// Represents what the service tells a caller over its channel: its API's address, token and all, which moves at each
/// rebind; or, to a caller who may not use the app, the refusal alone.
/// </summary>
public class DaemonChannelAnswer
{
    /// <summary>Gets the service's API address, with its token, or <see langword="null"/> with a refusal.</summary>
    public Uri? ApiUrl { get; init; }

    /// <summary>Gets the ID of the service's process, which the caller's peer check is given.</summary>
    public int ProcessId { get; init; }

    /// <summary>Gets the version of the service's binary, as <see cref="CurrentVersion"/> gives it.</summary>
    public required string Version { get; init; }

    /// <summary>Gets the sentence for a caller who may not use the app, or <see langword="null"/> for one who may.</summary>
    public string? Refusal { get; init; }

    [JsonIgnore]
    internal string? Token => ApiUrl == null ? null : LocalApiToken.Read(ApiUrl);

    /// <summary>Gets the version this binary is, as three numbers, which both sides send.</summary>
    public static string CurrentVersion { get; } =
        typeof(DaemonChannelAnswer).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
