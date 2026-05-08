using System.Net;

namespace VpnHood.Core.TcpStack.Abstractions;

/// <summary>
/// Represents an accepted TCP connection returned by an <see cref="ITcpListener"/>.
/// Analogous to <see cref="System.Net.Sockets.TcpClient"/> but for custom TCP stacks.
/// </summary>
public interface ITcpClient : IAsyncDisposable, IDisposable
{
    /// <summary>The local endpoint of this TCP connection.</summary>
    IPEndPoint LocalEndPoint { get; }

    /// <summary>The remote endpoint of this TCP connection.</summary>
    IPEndPoint RemoteEndPoint { get; }

    /// <summary>Gets the underlying <see cref="Stream"/> for reading and writing.</summary>
    Stream Stream { get; }
}
