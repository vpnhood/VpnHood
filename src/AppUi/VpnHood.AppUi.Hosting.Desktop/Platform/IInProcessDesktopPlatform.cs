using Microsoft.Extensions.Logging;
using VpnHood.AppUi.Hosting.Desktop.Abstractions;

namespace VpnHood.AppUi.Hosting.Desktop.Platform;

/// <summary>
/// Defines what a host without a service says for itself (<see cref="InProcessDesktopHost"/>): where the window keeps
/// things, how the system tells who owns the far end of a loopback connection, the process's log, and the app, which
/// runs in the window's process.
/// </summary>
public interface IInProcessDesktopPlatform
{
    /// <summary>Gets where the window keeps things.</summary>
    IDesktopUiPaths Paths { get; }

    /// <summary>Gets how the system tells who owns the far end of a loopback connection.</summary>
    ILoopbackPeerCheck PeerCheck { get; }

    /// <summary>Gets the desktop session this launch runs in, as the window's single instance asks it.</summary>
    IDesktopSession DesktopSession { get; }

    /// <summary>Gets a value indicating whether the window may keep a tray icon.</summary>
    bool UseTray { get; }

    /// <summary>Gets the system's ways for links and the clipboard where the UI's own are refused, or <see langword="null"/> for the UI's own.</summary>
    IDesktopShell? Shell { get; }

    /// <summary>
    /// Creates the process's log, once this launch holds the window, so a second launch, which only brings that window
    /// forward, leaves its file alone.
    /// </summary>
    /// <returns>The log's provider.</returns>
    /// <exception cref="InvalidOperationException">
    /// The platform cannot name the folder, as the Windows plug-in's outside its package.
    /// </exception>
    ILoggerProvider CreateLoggerProvider();

    /// <summary>Creates the app, once this launch holds the window, so a second launch builds none.</summary>
    /// <returns>The app; disposing it ends the app.</returns>
    /// <remarks>
    /// It throws what the app's start throws; the Windows plug-in's, for one, throws the exceptions below, and fails with an
    /// <see cref="Exception"/> where an ad provider names countries both to include and to exclude.
    /// </remarks>
    /// <exception cref="OverflowException">A duration in a built-in access key is out of range.</exception>
    /// <exception cref="System.IO.InvalidDataException">A built-in access key decodes to JSON <c>null</c>.</exception>
    /// <exception cref="System.Text.Json.JsonException">A built-in access key holds no valid access key.</exception>
    /// <exception cref="NotSupportedException">A built-in access key's version is not one the app reads.</exception>
    /// <exception cref="ArgumentNullException">The init params name no app name.</exception>
    /// <exception cref="FormatException">
    /// A built-in access key is no valid Base64, or an endpoint or a duration in it is not valid.
    /// </exception>
    /// <exception cref="ArgumentException">The init params' app name is empty.</exception>
    /// <exception cref="InvalidOperationException">
    /// The process does not run from its package, or the core app has started already.
    /// </exception>
    /// <exception cref="VpnHood.Core.Common.Exceptions.AnotherInstanceIsRunningException">
    /// Another instance of the app holds the lock.
    /// </exception>
    IAppDaemonHost CreateApp();
}
