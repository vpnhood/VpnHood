using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Devices.Windows.WinNative;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppLib.App.Windows;

// The service's single-instance lock: a named semaphore in an administrators' namespace of the app's
// own, named by its app id, which a standard user can neither make nor enter, so nobody takes the
// name first, as anyone could in the Global namespace; and one namespace in every session, where the
// service runs in session 0 and a daemon started by hand in a person's. A semaphore rather than a
// mutex, which belongs to the thread that took it and would be let go when that pool thread retires.
// The kernel drops it with the process, so a crash leaves no stale lock.
internal sealed class ServiceInstanceLock : IDisposable
{
    private const string SemaphoreName = "instance";

    private readonly AdministratorsNamespace _namespace;
    private readonly Semaphore _semaphore;
    private bool _disposed;

    private ServiceInstanceLock(AdministratorsNamespace administratorsNamespace, Semaphore semaphore)
    {
        _namespace = administratorsNamespace;
        _semaphore = semaphore;
    }

    public static ServiceInstanceLock Take(string appId)
    {
        var administratorsNamespace = AdministratorsNamespace.Open(appId);
        try {
            return new ServiceInstanceLock(administratorsNamespace, TakeSemaphore(administratorsNamespace, appId));
        }
        catch {
            administratorsNamespace.Dispose();
            throw;
        }
    }

    private static Semaphore TakeSemaphore(AdministratorsNamespace administratorsNamespace, string appId)
    {
        Semaphore semaphore;
        try {
            semaphore = new Semaphore(1, 1, administratorsNamespace.Name(SemaphoreName));
        }
        catch (UnauthorizedAccessException ex) {
            // it exists, and belongs to an instance this process may not even open
            throw new AnotherInstanceIsRunningException($"Another {appId} instance is already running.", ex);
        }

        if (semaphore.WaitOne(TimeSpan.Zero))
            return semaphore;

        semaphore.Dispose();
        throw new AnotherInstanceIsRunningException($"Another {appId} instance is already running.");
    }

    public void Dispose()
    {
        // the app's quick close may come while its graceful one ends with this
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        // a full count throws, as after a release by another process; the handles go all the same
        VhInvoker.TryInvoke("release the service's instance lock", LogLevel.Warning, () => _semaphore.Release());
        _semaphore.Dispose();
        _namespace.Dispose();
    }
}
