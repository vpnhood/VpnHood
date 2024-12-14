using VpnHood.Common.Messaging;

namespace VpnHood.Server.Access;

public class ServerCommand(string configCode)
{
    public string ConfigCode { get; set; } = configCode;
    public Dictionary<ulong, SessionResponse> SessionResponses { get; set; } = new();
}