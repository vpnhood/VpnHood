using System.Net.Sockets;
using VpnHood.AppUi.Hosting.Desktop.Abstractions;
using VpnHood.AppUi.Hosting.Desktop.Linux.Utils;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppUi.Hosting.Desktop.Linux;

// The channel on Linux: a Unix socket in a folder under /run only root may write, so nobody takes
// its name first. A caller must be root or in sudo, wheel or admin; a sudoers line alone is not seen.
public sealed class LinuxDaemonChannel(LinuxDesktopPaths paths) : IDaemonChannel
{
    private static readonly string[] AdminGroups = ["sudo", "wheel", "admin"];
    private const int SolSocket = 1;
    private const int SoPeerCred = 17;
    private const int SoPeerGroups = 59;

    public Task<IDaemonChannelListener> Listen(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(paths.RuntimePath);
        File.SetUnixFileMode(paths.RuntimePath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        if (File.Exists(paths.ChannelSocketPath))
            File.Delete(paths.ChannelSocketPath); // a previous run's, which nobody serves

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try {
            socket.Bind(new UnixDomainSocketEndPoint(paths.ChannelSocketPath));
            socket.Listen(16);

            // a connect needs write on the socket, which root's umask withholds; the caller check is the gate
            File.SetUnixFileMode(paths.ChannelSocketPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite);
            return Task.FromResult<IDaemonChannelListener>(
                new Listener(socket, paths.ChannelSocketPath, LinuxGroups.Resolve(AdminGroups)));
        }
        catch {
            socket.Dispose();
            throw;
        }
    }

    public async Task<Stream> Connect(CancellationToken cancellationToken)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(paths.ChannelSocketPath), cancellationToken).Vhc();
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch {
            socket.Dispose();
            throw;
        }
    }

    private sealed class Listener(Socket socket, string socketPath, IReadOnlySet<int> adminGroupIds) : IDaemonChannelListener
    {
        public async Task<IDaemonChannelCaller> Accept(CancellationToken cancellationToken)
        {
            var accepted = await socket.AcceptAsync(cancellationToken).Vhc();
            return new Caller(accepted, adminGroupIds);
        }

        public ValueTask DisposeAsync()
        {
            socket.Dispose();
            VhUtils.TryInvoke("remove the channel's socket", () => File.Delete(socketPath));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Caller : IDaemonChannelCaller
    {
        private readonly Socket _socket;
        private readonly IReadOnlySet<int> _adminGroupIds;

        public Caller(Socket socket, IReadOnlySet<int> adminGroupIds)
        {
            _socket = socket;
            _adminGroupIds = adminGroupIds;
            Stream = new NetworkStream(socket, ownsSocket: true);
        }

        public Stream Stream { get; }

        public DaemonCallerInfo Identify()
        {
            var (uid, gid) = ReadPeerCredentials();
            var isAdministrator = uid == 0 || _adminGroupIds.Contains(gid) || ReadPeerGroups().Any(_adminGroupIds.Contains);
            return new DaemonCallerInfo(LinuxUser.NameOf(uid), isAdministrator);
        }

        // struct ucred { pid_t pid; uid_t uid; gid_t gid; }
        private (int uid, int gid) ReadPeerCredentials()
        {
            Span<byte> buffer = stackalloc byte[12];
            var read = _socket.GetRawSocketOption(SolSocket, SoPeerCred, buffer);
            if (read < 12)
                throw new InvalidOperationException($"SO_PEERCRED answered {read} bytes.");

            return (BitConverter.ToInt32(buffer[4..8]), BitConverter.ToInt32(buffer[8..12]));
        }

        // the supplementary groups, up to a thousand of them; the primary one is in the credentials
        private int[] ReadPeerGroups()
        {
            var buffer = new byte[4096];
            var read = _socket.GetRawSocketOption(SolSocket, SoPeerGroups, buffer);
            var groups = new int[read / 4];
            for (var i = 0; i < groups.Length; i++)
                groups[i] = BitConverter.ToInt32(buffer, i * 4);
            return groups;
        }

        public async ValueTask DisposeAsync()
        {
            await Stream.DisposeAsync().Vhc();
        }
    }
}
