using System.Text.Json.Serialization;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Proxies.Management.Abstractions.Options;

namespace VpnHood.Core.Client.VpnServices.Abstractions;

/// <summary>
/// Represents what the app hands to the VPN service, in its config folder as
/// <see cref="ClientOptions.VpnConfigFileName"/>: the client's options, the proxies' options, and the words the service
/// shows itself.
/// </summary>
/// <remarks>
/// The client only ever receives the nested <see cref="ClientOptions"/>, so it stays blind to paths and stores; the
/// service turns the rest into services itself.
/// </remarks>
public class VpnServiceOptions
{
    /// <summary>
    /// Gets the contract version these options are written in (<see cref="VpnServiceContract"/>), which the engine
    /// refuses when it does not speak it. Options from before versions existed have none, and are the first. The
    /// default is 1.
    /// </summary>
    public int ContractVersion { get; init; } = 1;

    /// <summary>Gets or sets the client's options, the only part the client receives.</summary>
    public required ClientOptions ClientOptions { get; set; }

    /// <summary>
    /// Gets or sets the options of the proxies to connect through, or <see langword="null"/> for none.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ProxyOptions? ProxyOptions { get; set; }

    /// <summary>
    /// Gets or sets the words the service shows itself, as its notification's buttons, in the app's language.
    /// </summary>
    public VpnServiceLocalization Localization { get; set; } = new();
}
