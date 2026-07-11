using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Toolkit.Converters;

namespace VpnHood.Core.Server.Access;

public class ServerHostEndPointStatus
{
    public required ChannelProtocol Protocol { get; init; }

    [JsonConverter(typeof(IPEndPointConverter))]
    public required IPEndPoint EndPoint { get; init; }

    /// <summary>null means success; non-null contains the serialized ApiError.</summary>
    public ApiError? Error { get; init; }
}
