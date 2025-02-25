using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Toolkit.Converters;

namespace VpnHood.Core.Common.Messaging;

public class SessionResponse
{
    public required SessionErrorCode ErrorCode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? ErrorMessage { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public AccessUsage? AccessUsage { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public SessionSuppressType SuppressedBy { get; set; }

    [JsonConverter(typeof(IPEndPointConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public IPEndPoint? RedirectHostEndPoint { get; set; }

    [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public IPEndPoint[]? RedirectHostEndPoints { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? AccessKey { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? ClientCountry { get; set; }
}