namespace VpnHood.AppUi.Hosting.Desktop.Platform;

/// <summary>Defines the app as a platform hosts a headless one: creating one starts it, and disposing one ends it.</summary>
/// <remarks>
/// Disposing it asynchronously tries to take a connected tunnel down before the process goes, where the tunnel ends
/// with the app; a tunnel the system holds, as the Windows plug-in's, stays up. Disposing it at once releases what the
/// app holds with no goodbye, for a stop whose time has run out.
/// </remarks>
public interface IAppDaemonHost : IAsyncDisposable, IDisposable;
