using VpnHood.Common.Messaging;

namespace VpnHood.Tunneling.Messaging;

public class HelloRequest() 
    : ClientRequest((byte)Messaging.RequestCode.Hello)
{
    public required string TokenId { get; init; }
    public required ClientInfo ClientInfo { get; init; }
    public required byte[] EncryptedClientId { get; init; }
    public string? ServerLocation { get; init; } // format: countryCode/region/city
    public bool AllowRedirect { get; init; } = true;
    public bool? IsIpV6Supported { get; init; }
}