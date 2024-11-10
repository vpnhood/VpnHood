using VpnHood.Common.Messaging;

namespace VpnHood.Server.Access;

public class ServerCommand(string configCode)
{
    public string ConfigCode { get; set; } = configCode;
    public Dictionary<long, SessionResponse> Sessions { get; set; } = new();
}