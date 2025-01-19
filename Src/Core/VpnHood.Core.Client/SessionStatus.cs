using VpnHood.Core.Common.ApiClients;
using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Client;

public class SessionStatus
{
    public SessionErrorCode ErrorCode { get; internal set; }
    public SessionSuppressType SuppressedTo { get; internal set; }
    public SessionSuppressType SuppressedBy { get; internal set; }
    public AccessInfo? AccessInfo { get; internal set; }
    public AccessUsage? AccessUsage { get; internal set; }
    public ApiError? Error { get; internal set; }
}