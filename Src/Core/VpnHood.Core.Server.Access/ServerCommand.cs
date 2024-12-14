using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Server.Access;

public class ServerCommand(string configCode)
{
    public string ConfigCode { get; set; } = configCode;
    public Dictionary<ulong, SessionResponse> SessionResponses { get; set; } = new();
}