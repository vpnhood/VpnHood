using System.Text.Json.Serialization;
using VpnHood.Common.Messaging;

namespace VpnHood.Server.Messaging;

public class SessionSessionResponseEx : SessionSessionResponse
{
    [JsonConstructor]
    public SessionSessionResponseEx(SessionErrorCode errorCode) : base(errorCode)
    {
    }
}