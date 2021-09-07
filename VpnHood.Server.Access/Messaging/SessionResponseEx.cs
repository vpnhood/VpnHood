using System.Text.Json.Serialization;
using VpnHood.Common.Messaging;

namespace VpnHood.Server.Messaging
{
    public class SessionResponseEx : SessionResponse
    {
        [JsonConstructor]
        public SessionResponseEx(SessionErrorCode errorCode) : base(errorCode)
        {
        }
    }
}