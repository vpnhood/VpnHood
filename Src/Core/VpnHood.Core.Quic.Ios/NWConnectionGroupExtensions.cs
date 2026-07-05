using Network;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Quic.Ios;

/// <summary>
/// Best-effort teardown helpers for <see cref="NWConnectionGroup"/>. Network.framework objects can throw
/// from their native trampolines when detached or cancelled mid-flight (e.g. after the group has already
/// failed/cancelled). During cleanup, we only want the call attempted, never to mask the original failure,
/// so each helper swallows and logs via <see cref="VhUtils.TryInvoke(System.Action)"/>.
/// </summary>
internal static class NWConnectionGroupExtensions
{
    extension(NWConnectionGroup connectionGroup)
    {
        /// <summary>
        /// Best-effort <see cref="NWConnectionGroup.SetStateChangedHandler"/>. Pass <c>null</c> to detach the
        /// current handler.
        /// </summary>
        public void TrySetStateChangedHandler(NWConnectionGroupStateChangedDelegate? handler)
            => VhUtils.TryInvoke(() => connectionGroup.SetStateChangedHandler(handler!));

        /// <summary>
        /// Best-effort <see cref="NWConnectionGroup.SetNewConnectionHandler"/>. Pass <c>null</c> to detach the
        /// current handler.
        /// </summary>
        public void TrySetNewConnectionHandler(Action<NWConnection>? handler)
            => VhUtils.TryInvoke(() => connectionGroup.SetNewConnectionHandler(handler!));

        /// <summary>Best-effort <see cref="NWConnectionGroup.Cancel"/>.</summary>
        public void TryCancel()
            => VhUtils.TryInvoke(connectionGroup.Cancel);
    }
}
