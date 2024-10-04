using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.AccessServer.Dtos.ServerFarms;

public class AccessPointView
{
    public required Guid ServerFarmId { get; init; }
    public required Guid ServerId { get; init; }
    public required string ServerName { get; init; }

    [JsonConverter(typeof(IPEndPointConverter))]
    public required IPEndPoint TcpEndPoint { get; init; }
}