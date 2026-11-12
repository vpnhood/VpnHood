using System.Text;
using VpnHood.AppUi.Hosting.Desktop.Channel;
using VpnHood.AppUi.Hosting.Desktop.Daemon.Platform;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Desktop.Daemon.Channel;

/// <summary>Provides a client's side of the service's channel: say hello, read the answer.</summary>
public static class DaemonChannelClient
{
    /// <summary>Says hello over an open connection, and reads the first answer.</summary>
    /// <param name="stream">The connection's stream, which the hello is written to.</param>
    /// <param name="reader">The reader over the same stream.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the answer, or <see langword="null"/> where the service closed the connection before answering.</returns>
    internal static async Task<DaemonChannelAnswer?> Ask(Stream stream, StreamReader reader, CancellationToken cancellationToken)
    {
        await DaemonChannelLines.Write(stream, new DaemonChannelHello { Version = DaemonChannelAnswer.CurrentVersion },
            cancellationToken).Vhc();
        return await DaemonChannelLines.Read<DaemonChannelAnswer>(reader, cancellationToken).Vhc();
    }

    /// <summary>Asks the service once, over a connection of its own.</summary>
    /// <param name="channel">The service's channel.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the answer.</returns>
    /// <exception cref="System.Net.Sockets.SocketException">On Linux, the channel's socket could not be reached.</exception>
    /// <exception cref="TimeoutException">On Windows, the channel did not answer in time.</exception>
    /// <exception cref="IOException">The service closed the channel before answering.</exception>
    /// <exception cref="InvalidOperationException">
    /// On Windows, the service has not opened its channel, or someone not trusted serves it.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static async Task<DaemonChannelAnswer> AskOnce(IDaemonChannel channel, CancellationToken cancellationToken)
    {
        await using var stream = await channel.Connect(cancellationToken).Vhc();
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        return await Ask(stream, reader, cancellationToken).Vhc() ??
               throw new IOException("The service closed the channel before answering.");
    }
}
