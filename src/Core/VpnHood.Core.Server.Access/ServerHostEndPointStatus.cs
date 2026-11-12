using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Common.Messaging;
using VpnHood.Net.Toolkit.ApiClients;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Core.Server.Access;

/// <summary>
/// Represents how a listener of the server fared at its configuration.
/// </summary>
public class ServerHostEndPointStatus
{
    /// <summary>Gets the listener's protocol.</summary>
    public required ChannelProtocol Protocol { get; init; }

    /// <summary>Gets the listener's endpoint.</summary>
    [JsonConverter(typeof(IPEndPointConverter))]
    public required IPEndPoint EndPoint { get; init; }

    /// <summary>Gets the error the listener failed with, or <see langword="null"/> where it listens.</summary>
    public ApiError? Error { get; init; }
}
