using System.Text.Json.Serialization;

namespace VpnHood.Common.Messaging
{
    public class SessionResponseEx : SessionResponse
    {
        [JsonConstructor]
        public SessionResponseEx(SessionErrorCode errorCode) : base(errorCode)
        {
        }
    }
}
