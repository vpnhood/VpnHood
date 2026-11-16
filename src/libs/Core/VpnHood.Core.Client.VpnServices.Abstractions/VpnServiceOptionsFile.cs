using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Client.VpnServices.Abstractions;

/// <summary>
/// Represents the options a VPN service runs with, saved in its config folder as
/// <see cref="ClientOptions.VpnConfigFileName"/>.
/// </summary>
/// <param name="configFolder">The VPN service's config folder.</param>
/// <remarks>
/// The app writes them at each connect and makes them again at each change of its settings or servers; the service
/// reads them at each start, including one the app does not make — always-on, the quick-settings tile, the system's VPN
/// settings, the system restarting the service — which then runs with what the app shows. As the two processes share
/// the file, an access that finds it held by the other tries again for up to 2 seconds.
/// </remarks>
public class VpnServiceOptionsFile(string configFolder)
{
    // the VPN service reads them at its start, in a process of its own, while the app may be writing them
    private static readonly TimeSpan SharingTimeout = TimeSpan.FromSeconds(2);
    private readonly AsyncLock _writeLock = new();

    /// <summary>Gets the file's path.</summary>
    public string FilePath { get; } = Path.Combine(configFolder, ClientOptions.VpnConfigFileName);

    /// <summary>Gets a value indicating whether the file exists.</summary>
    public bool Exists => File.Exists(FilePath);

    /// <summary>Reads the options.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the options.</returns>
    /// <exception cref="FileNotFoundException">There is no file.</exception>
    /// <exception cref="IOException">
    /// The file could not be reached, as where the other process still holds it after 2 seconds.
    /// </exception>
    /// <exception cref="System.Text.Json.JsonException">The file is not JSON of the options.</exception>
    /// <exception cref="InvalidDataException">The file holds <c>null</c>.</exception>
    /// <exception cref="UnauthorizedAccessException">The file may not be read.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task<VpnServiceOptions> Read(CancellationToken cancellationToken)
    {
        var json = await FileUtils.WaitOutSharingAsync(() => File.ReadAllTextAsync(FilePath, cancellationToken),
            SharingTimeout, cancellationToken).Vhc();
        return JsonUtils.Deserialize<VpnServiceOptions>(json);
    }

    /// <summary>Reads the options where there is a file that can be read.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task whose result is the options, or <see langword="null"/> where there is no file or it cannot be read, which
    /// it logs.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task<VpnServiceOptions?> TryRead(CancellationToken cancellationToken)
    {
        if (!Exists)
            return null;

        try {
            return await Read(cancellationToken).Vhc();
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not read the VPN service options. FilePath: {FilePath}", FilePath);
            return null;
        }
    }

    /// <summary>Writes the options, replacing the file whole, so the service never reads a part of it.</summary>
    /// <param name="serviceOptions">The options.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the file is written.</returns>
    /// <exception cref="IOException">
    /// The file could not be reached, as where the other process still holds it after 2 seconds.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">The file may not be written.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task Write(VpnServiceOptions serviceOptions, CancellationToken cancellationToken)
    {
        using var scopeLock = await _writeLock.LockAsync(cancellationToken).Vhc();
        await WriteFile(serviceOptions, cancellationToken).Vhc();
    }

    /// <summary>
    /// Writes options made again from the app's settings, connected or not, where there is a file, which only a connect
    /// makes; without one, it does nothing.
    /// </summary>
    /// <param name="serviceOptions">
    /// The options. What only a connect knows, its plan (<see cref="ClientOptions.PlanId"/>) and whether always-on made
    /// it (<see cref="ClientOptions.IsAlwaysOn"/>), is set to the saved options'.
    /// </param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the file is written, or found missing.</returns>
    /// <remarks>
    /// The saved options are read under the lock this instance's writes take, so a connect's write in between is kept.
    /// </remarks>
    /// <exception cref="IOException">
    /// The file could not be reached, as where the other process still holds it after 2 seconds.
    /// </exception>
    /// <exception cref="System.Text.Json.JsonException">The saved file is not JSON of the options.</exception>
    /// <exception cref="InvalidDataException">The saved file holds <c>null</c>.</exception>
    /// <exception cref="UnauthorizedAccessException">The file may not be read or written.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task Update(VpnServiceOptions serviceOptions, CancellationToken cancellationToken)
    {
        using var scopeLock = await _writeLock.LockAsync(cancellationToken).Vhc();
        if (!Exists)
            return;

        var savedOptions = await Read(cancellationToken).Vhc();
        serviceOptions.ClientOptions.PlanId = savedOptions.ClientOptions.PlanId;
        serviceOptions.ClientOptions.IsAlwaysOn = savedOptions.ClientOptions.IsAlwaysOn;
        await WriteFile(serviceOptions, cancellationToken).Vhc();
    }

    /// <summary>Deletes the file; one already gone is no error.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the file is gone.</returns>
    /// <exception cref="IOException">
    /// The file could not be reached, as where the other process still holds it after 2 seconds.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">The file may not be deleted.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task Delete(CancellationToken cancellationToken)
    {
        using var scopeLock = await _writeLock.LockAsync(cancellationToken).Vhc();
        await FileUtils.WaitOutSharingAsync(() => {
            File.Delete(FilePath);
            return Task.CompletedTask;
        }, SharingTimeout, cancellationToken).Vhc();
    }

    // replaced whole, so the service never reads a part of it; the replace is tried again while the service reads
    private async Task WriteFile(VpnServiceOptions serviceOptions, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(serviceOptions);
        await FileUtils.WaitOutSharingAsync(() => FileUtils.WriteAllTextAtomicAsync(FilePath, json, cancellationToken),
            SharingTimeout, cancellationToken).Vhc();
    }
}
