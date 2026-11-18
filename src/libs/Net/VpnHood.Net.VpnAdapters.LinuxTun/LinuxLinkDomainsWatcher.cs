using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Net.VpnAdapters.LinuxTun;

// Tells when a link's DNS settings may have changed, for LinuxTunVpnAdapter to look at the links' domains again while
// it keeps them off. systemd-resolved raises no signal for a link's domains (its PropertiesChanged tells of its
// servers alone; seen on systemd 249 and 259), so the watch is on the folders where such a change is written as it is
// made: systemd-resolved's own, where it rewrites resolv.conf and keeps, under netif, the settings a link got over the
// bus, as NetworkManager's and resolvectl's; and the one where systemd-networkd writes each of its links. What a file
// holds is never read: any change says "look again".
internal sealed class LinuxLinkDomainsWatcher : IDisposable
{
    private readonly Lock _lock = new();
    private readonly TimeSpan _settleTime;
    private readonly Action _look;
    private readonly Timer _timer;
    private readonly List<FileSystemWatcher> _watchers = [];
    private bool _isLookPending;
    private bool _disposed;

    /// <summary>
    /// Starts the watch.
    /// </summary>
    /// <param name="folders">The folders to watch, with the folders under them. One that is not there is passed over.</param>
    /// <param name="settleTime">
    /// How long after a change the look comes: a manager sets a link's servers, its domains and the rest one after
    /// another, and one look serves them all.
    /// </param>
    /// <param name="look">
    /// Called on a thread of the pool, a settle time after a change, and after an error of the watch. It must not
    /// throw.
    /// </param>
    /// <remarks>
    /// The system may refuse the watch of a folder by its error, at the start or later, which is logged as a warning:
    /// changes it would have told of are not told.
    /// </remarks>
    /// <exception cref="IOException">None of the folders is there, or the system refused the watch.</exception>
    public LinuxLinkDomainsWatcher(IReadOnlyList<string> folders, TimeSpan settleTime, Action look)
    {
        _settleTime = settleTime;
        _look = look;
        _timer = new Timer(_ => Look(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        try {
            foreach (var folder in folders.Where(Directory.Exists))
                _watchers.Add(CreateWatcher(folder));

            if (_watchers.Count == 0)
                throw new DirectoryNotFoundException(
                    $"None of the folders that tell of the interfaces' DNS settings is there. Folders: {string.Join(", ", folders)}");
        }
        catch {
            Dispose();
            throw;
        }
    }

    private FileSystemWatcher CreateWatcher(string folder)
    {
        var watcher = new FileSystemWatcher(folder) {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
        };

        try {
            watcher.Changed += (_, _) => OnChanged();
            watcher.Created += (_, _) => OnChanged();
            watcher.Deleted += (_, _) => OnChanged();
            watcher.Renamed += (_, _) => OnChanged();
            watcher.Error += Watcher_Error;
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch {
            watcher.Dispose();
            throw;
        }
    }

    // The first change sets the look's time, and the ones that follow it do not move it: put off by each, a steady
    // run of writes would keep the look from ever coming. Nothing once the watch has ended.
    private void OnChanged()
    {
        lock (_lock) {
            if (_disposed || _isLookPending)
                return;

            _isLookPending = true;
            _timer.Change(_settleTime, Timeout.InfiniteTimeSpan);
        }
    }

    // No longer pending before the look, so a change that comes while it runs brings another
    private void Look()
    {
        lock (_lock) {
            if (_disposed)
                return;

            _isLookPending = false;
        }

        _look();
    }

    // Changes lost as they came faster than they were read leave the watch at work: a look sees what they were. Any
    // other error may have ended the watch of a folder, as the system's limit of watches does for a folder made later,
    // which nothing tells apart, so it is said as such; the run goes on, as with a domain a link keeps. A look either
    // way, for what changed before it.
    private void Watcher_Error(object sender, ErrorEventArgs e)
    {
        var ex = e.GetException();
        if (ex is InternalBufferOverflowException)
            VhLogger.Instance.LogWarning(ex,
                "The watch on the interfaces' DNS settings lost changes. Looking at the interfaces again.");
        else
            VhLogger.Instance.LogWarning(ex,
                "The watch on the interfaces' DNS settings has failed, so a DNS domain that a network sets later may " +
                "be looked up outside the VPN.");

        OnChanged();
    }

    public void Dispose()
    {
        lock (_lock) {
            if (_disposed)
                return;

            _disposed = true;
            _timer.Dispose();
        }

        foreach (var watcher in _watchers)
            watcher.Dispose();
    }
}
