using VpnHood.AppUi.Hosting.Desktop.Platform;

namespace VpnHood.AppUi.Hosting.Desktop.Daemon.Platform;

/// <summary>
/// Defines how a platform builds the app for the two commands that hold it: <c>daemon</c>, the service, and <c>dev</c>,
/// a debugger's run with the window in one process.
/// </summary>
public interface IAppDaemonHostFactory
{
    /// <summary>Creates the service's app, in its own storage.</summary>
    /// <returns>The app.</returns>
    /// <exception cref="OverflowException">A duration in a built-in access key is out of range.</exception>
    /// <exception cref="InvalidDataException">A built-in access key decodes to JSON <c>null</c>.</exception>
    /// <exception cref="System.Text.Json.JsonException">A built-in access key holds no valid access key.</exception>
    /// <exception cref="NotSupportedException">A built-in access key's version is not one the app reads.</exception>
    /// <exception cref="FormatException">
    /// A built-in access key is no valid Base64, or an endpoint or a duration in it is not valid.
    /// </exception>
    /// <exception cref="System.ComponentModel.Win32Exception">
    /// On Windows, the service's lock or the job the VPN host runs in could not be made.
    /// </exception>
    /// <exception cref="VpnHood.Core.Common.Exceptions.AnotherInstanceIsRunningException">
    /// Another instance of the app holds the lock.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// This process may not be the service, the app has started already, or the app's start found what it needs missing, as
    /// a folder the platform cannot name.
    /// </exception>
    /// <remarks>
    /// It throws what the app's start throws, as below; an ad provider that names countries both to include and to exclude
    /// fails it with an <see cref="Exception"/>.
    /// </remarks>
    IAppDaemonHost CreateService();

    /// <summary>Creates a debugger's app, in the storage it is handed, as whoever runs it.</summary>
    /// <param name="storagePath">The app's storage.</param>
    /// <returns>The app.</returns>
    /// <exception cref="OverflowException">A duration in a built-in access key is out of range.</exception>
    /// <exception cref="InvalidDataException">A built-in access key decodes to JSON <c>null</c>.</exception>
    /// <exception cref="System.Text.Json.JsonException">A built-in access key holds no valid access key.</exception>
    /// <exception cref="NotSupportedException">A built-in access key's version is not one the app reads.</exception>
    /// <exception cref="FormatException">
    /// A built-in access key is no valid Base64, or an endpoint or a duration in it is not valid.
    /// </exception>
    /// <exception cref="System.ComponentModel.Win32Exception">On Windows, the job the VPN host runs in could not be made.</exception>
    /// <exception cref="VpnHood.Core.Common.Exceptions.AnotherInstanceIsRunningException">
    /// Another instance of the app holds the lock.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The app has started already, or the app's start found what it needs missing.
    /// </exception>
    /// <remarks>
    /// It throws what the app's start throws, as below; an ad provider that names countries both to include and to exclude
    /// fails it with an <see cref="Exception"/>.
    /// </remarks>
    IAppDaemonHost CreateDev(string storagePath);
}
