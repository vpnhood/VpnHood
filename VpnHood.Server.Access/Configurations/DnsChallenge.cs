namespace VpnHood.Server.Access.Configurations;

public class DnsChallenge
{
    public required string Token { get; init; }
    public required string KeyAuthorization { get; init; }
}