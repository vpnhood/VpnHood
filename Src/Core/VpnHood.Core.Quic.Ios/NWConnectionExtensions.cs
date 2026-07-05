using Network;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Quic.Ios;

/// <summary>
/// Best-effort teardown helpers for a single QUIC-stream <see cref="NWConnection"/>. Network.framework
/// objects can throw from their native trampolines when detached or cancelled mid-flight (e.g. after the
/// stream has already failed/cancelled). During cleanup, we only want the call attempted, never to mask the
/// original failure, so each helper swallows and logs via <see cref="VhUtils.TryInvoke(System.Action)"/>.
/// </summary>
internal static class NWConnectionExtensions
{
    extension(NWConnection connection)
    {
        /// <summary>
        /// Best-effort <see cref="NWConnection.SetStateChangeHandler"/>. Pass <c>null</c> to detach the
        /// current handler.
        /// </summary>
        public void TrySetStateChangeHandler(Action<NWConnectionState, NWError>? handler)
            => VhUtils.TryInvoke(() => connection.SetStateChangeHandler(handler!));

        /// <summary>Best-effort <see cref="NWConnection.Cancel"/>.</summary>
        public void TryCancel()
            => VhUtils.TryInvoke(connection.Cancel);
    }
}
