using System.Text.Json;
using Android.Content.PM;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Devices.Abstractions;
using VpnHood.Core.Client.Devices.Android.Utils;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;
using AndroidBuild = Android.OS.Build;

namespace VpnHood.Core.Client.Devices.Android.InstalledApps;

// The apps a split by app can pick from. Loading their names and icons takes Android seconds, most of
// it reading each app's files from storage, so they are kept in a file: a build loads only the apps
// installed or updated since, and still asks Android which apps are there and shown.
internal sealed class AndroidInstalledApps
{
    // the app list renders the icons at 30 css pixels, so this keeps them crisp on high density
    // screens while bounding the encoded payload, as the list may contain hundreds of apps
    private const int IconSize = 64;

    // raised when what an item holds changes, so the items of an older build are loaded again
    private const int CacheVersion = 1;

    // more gain nothing: Android's package and resource services serialize much of this work
    private const int MaxLoadThreads = 4;

    // What the update times miss, such as a theme that restyles other apps' icons, is put right within
    // this time; each lifetime costs one load of every app
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(28);

    private readonly AsyncLock _buildLock = new();
    private readonly string _cacheFilePath = Path.Combine(
        Application.Context.CacheDir?.AbsolutePath ??
        throw new InvalidOperationException("Android named no cache folder for the app."),
        "installed-apps.json");

    public async Task<IReadOnlyList<DeviceAppInfo>> GetInstalledApps(CancellationToken cancellationToken)
    {
        // one build at a time, so the next one finds what this one loaded
        using var buildLock = await _buildLock.LockAsync(cancellationToken).Vhc();

        // off the caller's thread: in process the caller is the UI's, and Android's calls block
        return await Task.Run(() => Build(cancellationToken), cancellationToken).Vhc();
    }

    private async Task<IReadOnlyList<DeviceAppInfo>> Build(CancellationToken cancellationToken)
    {
        var packageManager = Application.Context.PackageManager ??
                             throw new InvalidOperationException("Could not acquire PackageManager.");
        var configuration = GetConfiguration();
        var cache = await ReadCache(configuration, cancellationToken).Vhc();
        var cachedItems = cache?.Items.ToDictionary(x => x.AppId) ?? new Dictionary<string, InstalledAppsCacheItem>();
        var currentAppId = Application.Context.PackageName;
        var packages = packageManager.GetInstalledPackages(0)
            .Where(x => x.PackageName != currentAppId)
            .ToArray();

        var items = new InstalledAppsCacheItem?[packages.Length];
        var parallelOptions = new ParallelOptions {
            MaxDegreeOfParallelism = Math.Min(MaxLoadThreads, System.Environment.ProcessorCount),
            CancellationToken = cancellationToken
        };
        await Parallel.ForEachAsync(Enumerable.Range(0, packages.Length), parallelOptions, (index, _) => {
            items[index] = GetItem(packageManager, packages[index], cachedItems);
            return ValueTask.CompletedTask;
        }).Vhc();

        var newItems = items
            .OfType<InstalledAppsCacheItem>()
            .DistinctBy(x => x.AppId)
            .OrderBy(x => x.AppName)
            .ToArray();

        // a file that is kept keeps its date, so the lifetime counts from the load of every app
        if (IsChanged(newItems, cachedItems))
            await WriteCache(new InstalledAppsCache {
                Configuration = configuration,
                CreatedTime = cache?.CreatedTime ?? DateTime.UtcNow,
                Items = newItems
            }, cancellationToken).Vhc();

        return newItems
            .Select(x => new DeviceAppInfo { AppId = x.AppId, AppName = x.AppName, IconPng = x.IconPng })
            .ToArray();
    }

    // null for an app the list leaves out; an app's name and icon change only with its package
    private static InstalledAppsCacheItem? GetItem(PackageManager packageManager, PackageInfo package,
        IReadOnlyDictionary<string, InstalledAppsCacheItem> cachedItems)
    {
        using var appInfo = package.ApplicationInfo;
        if (appInfo?.PackageName is not { } appId || !IsVisibleApp(packageManager, appInfo))
            return null;

        if (cachedItems.TryGetValue(appId, out var cachedItem) && cachedItem.UpdateTime == package.LastUpdateTime)
            return cachedItem;

        return LoadItem(packageManager, appInfo, appId, package.LastUpdateTime);
    }

    private static InstalledAppsCacheItem? LoadItem(PackageManager packageManager, ApplicationInfo appInfo,
        string appId, long updateTime)
    {
        var appName = appInfo.LoadLabel(packageManager);
        if (string.IsNullOrWhiteSpace(appName) || appName == appId)
            return null;

        using var icon = appInfo.LoadIcon(packageManager);
        if (icon is null)
            return null;

        return new InstalledAppsCacheItem {
            AppId = appId,
            UpdateTime = updateTime,
            AppName = appName,
            IconPng = icon.DrawableEncodeToBase64(IconSize)
        };
    }

    private static bool IsVisibleApp(PackageManager packageManager, ApplicationInfo appInfo)
    {
        var appId = appInfo.PackageName;

        // do not use IsEnabled as app may just suspend by the system

        if (string.IsNullOrWhiteSpace(appId))
            return false;

        // Does it have a Launcher icon? (Most user apps)
        if (packageManager.GetLaunchIntentForPackage(appId) != null)
            return true;

        // Is it an updated system app? (e.g., Chrome, Maps, YouTube)
        if ((appInfo.Flags & ApplicationInfoFlags.UpdatedSystemApp) != 0)
            return true;

        // Is it a known "Core" tool ?
        if (appId == "com.google.android.projection.gearhead" || // Android Auto
            appId == "com.google.android.embedded.projection") // Embedded Projection
            return true;

        // Is it a non-system app?
        if ((appInfo.Flags & ApplicationInfoFlags.System) == 0)
            return true;

        return false;
    }

    // What every app's name and icon depend on besides the app: the languages, the screen's density and
    // the system's build. Not dark mode: an app's icon rarely follows it, and a dark mode on a schedule
    // would load every app again twice a day.
    private static string GetConfiguration()
    {
        var configuration = Application.Context.Resources?.Configuration ??
                            throw new InvalidOperationException("Android gave the app no configuration.");
        return string.Join('|', CacheVersion, IconSize, configuration.Locales.ToLanguageTags(),
            configuration.DensityDpi, AndroidBuild.Fingerprint);
    }

    // the file is written only when an app came, went or changed
    private static bool IsChanged(IReadOnlyList<InstalledAppsCacheItem> items,
        IReadOnlyDictionary<string, InstalledAppsCacheItem> cachedItems)
    {
        return items.Count != cachedItems.Count ||
               items.Any(x => !cachedItems.TryGetValue(x.AppId, out var cachedItem) ||
                              cachedItem.UpdateTime != x.UpdateTime);
    }

    // Null when there is no file to use: none yet, an unreadable one, one made under another
    // configuration or past its lifetime; every app is then loaded again and the file written anew. A
    // date ahead of the clock means the clock went back, and the file starts over too.
    private async Task<InstalledAppsCache?> ReadCache(string configuration, CancellationToken cancellationToken)
    {
        if (!File.Exists(_cacheFilePath))
            return null;

        try {
            var json = await File.ReadAllTextAsync(_cacheFilePath, cancellationToken).Vhc();
            var cache = JsonUtils.Deserialize<InstalledAppsCache>(json);
            var age = DateTime.UtcNow - cache.CreatedTime;
            return cache.Configuration == configuration && age >= TimeSpan.Zero && age < CacheLifetime
                ? cache
                : null;
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not read the installed apps' file. Every app is loaded again.");
            return null;
        }
    }

    // a file that cannot be written costs only speed: the list still shows
    private async Task WriteCache(InstalledAppsCache cache, CancellationToken cancellationToken)
    {
        try {
            await FileUtils.WriteAllTextAtomicAsync(_cacheFilePath, JsonSerializer.Serialize(cache),
                cancellationToken).Vhc();
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not write the installed apps' file.");
        }
    }
}
