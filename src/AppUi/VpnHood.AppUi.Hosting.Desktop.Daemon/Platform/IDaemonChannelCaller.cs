namespace VpnHood.AppUi.Hosting.Desktop.Daemon.Platform;

/// <summary>Defines one caller on the channel: its stream, and who it is, as the platform reads it off the connection.</summary>
public interface IDaemonChannelCaller : IAsyncDisposable
{
    /// <summary>Gets the caller's stream.</summary>
    Stream Stream { get; }

    /// <summary>Reads who the caller is.</summary>
    /// <returns>Who the caller is, and whether they may use the app.</returns>
    /// <remarks>
    /// It is asked only after the caller's first line was read, as Windows lets the service read its caller only once
    /// something has come over the pipe.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The platform could not read the caller.</exception>
    DaemonCallerInfo Identify();
}
