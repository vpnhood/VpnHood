using VpnHood.Core.Client;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Test.Extensions;

public static class VpnHoodClientExtensions
{
    extension(VpnHoodClient client)
    {
        public Exception? GetSessionException()
        {
            var error = client.Session?.Status.Error;
            var exception = error is null ? null : ClientExceptionConverter.ApiErrorToException(error);
            return exception;
        }

        public SessionResponse? GetSessionExceptionResponse()
        {
            var response = client.GetSessionException() as SessionException;
            return response?.SessionResponse;
        }

        public SessionErrorCode GetSessionErrorCode()
        {
            var errorCode = client.GetSessionExceptionResponse()?.ErrorCode;
            return errorCode ?? SessionErrorCode.Ok;
        }

        public Task WaitForState(ClientState clientState, CancellationToken cancellationToken, int timeout = 6000,
            bool useUpdateStatus = false)
        {
            return VhTestUtil.AssertEqualsWait(clientState,
                async () => {
                    if (useUpdateStatus)
                        try {
                            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                            timeoutCts.CancelAfter(timeout);
                            await client.UpdateSessionStatus(timeoutCts.Token);
                        }
                        catch when (!cancellationToken.IsCancellationRequested) {
                            /*ignore*/
                        }

                    return client.State;
                },
                "Client state didn't reach the expected value.",
                timeout, cancellationToken: cancellationToken);
        }

        public ulong SessionId => ulong.Parse(client.RequiredSession.Info.SessionId);
    }
}