using System.Text;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Cli.Channel;

// A client's side of the line protocol: say hello, read the answer.
public static class DaemonChannelClient
{
    // The first answer over an open connection. Null when the service closed it before answering.
    public static async Task<DaemonChannelAnswer?> Ask(Stream stream, StreamReader reader, CancellationToken cancellationToken)
    {
        await DaemonChannelLines.Write(stream, new DaemonChannelHello { Version = DaemonChannelAnswer.CurrentVersion },
            cancellationToken).Vhc();
        return await DaemonChannelLines.Read<DaemonChannelAnswer>(reader, cancellationToken).Vhc();
    }

    // One question and one answer over a connection of its own; throws, saying why, when nothing
    // trusted answers.
    public static async Task<DaemonChannelAnswer> AskOnce(IDaemonChannel channel, CancellationToken cancellationToken)
    {
        await using var stream = await channel.Connect(cancellationToken).Vhc();
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        return await Ask(stream, reader, cancellationToken).Vhc() ??
               throw new IOException("The service closed the channel before answering.");
    }
}
