using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Server;

// takes the access manager's replies to the usage reports, in the order their requests went, each with when its
// request went out (a Stopwatch timestamp), so a session can tell a stale reply
internal interface ISessionResponseHandler
{
    void ApplySessionResponses(Dictionary<ulong, SessionResponse> sessionResponses, long requestTimestamp);
}
