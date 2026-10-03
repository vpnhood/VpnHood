using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Server;

// takes the access manager's replies to the usage reports, in the order their requests went
internal interface ISessionResponseHandler
{
    void ApplySessionResponses(Dictionary<ulong, SessionResponse> sessionResponses);
}
