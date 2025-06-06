using VpnHood.Core.Client;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Test;

public static class VpnHoodClientExtensions
{
    public static ISessionStatus GetSessionStatus(this VpnHoodClient client)
        => client.SessionStatus ??
           throw new InvalidOperationException("Session has not been initialized yet.");

    public static SessionErrorCode GetLastSessionErrorCode(this VpnHoodClient client)
        => (client.LastException as SessionException)?.SessionResponse.ErrorCode ?? SessionErrorCode.Ok;

    public static Task WaitForState(this VpnHoodClient client, ClientState clientState, int timeout = 6000,
        bool useUpdateStatus = false)
    {
        return VhTestUtil.AssertEqualsWait(clientState,
            async () => {
                if (useUpdateStatus)
                    try {
                        using var timeoutCts = new CancellationTokenSource(timeout);
                        await client.UpdateSessionStatus(timeoutCts.Token);
                    }
                    catch {
                        /*ignore*/
                    }

                return client.State;
            },
            "Client state didn't reach the expected value.",
            timeout);
    }


}