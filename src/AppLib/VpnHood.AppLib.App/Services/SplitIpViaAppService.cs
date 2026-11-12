using VpnHood.AppLib.App.Premium;
using VpnHood.AppLib.App.Settings;
using VpnHood.AppLib.App.Utils;
using VpnHood.Core.Filtering.Sqlite;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppLib.App.Services;

/// <summary>
/// Provides the database of the split by IP address that the app applies, built before a connect from the person's
/// three lists: the addresses to tunnel only, to leave outside and to drop.
/// </summary>
/// <param name="settingsService">The app's settings.</param>
/// <param name="premiumFeatureChecker">The premium gate.</param>
public class SplitIpViaAppService(AppSettingsService settingsService, IPremiumFeatureChecker premiumFeatureChecker)
{
    internal event EventHandler? StateChanged;

    internal bool IsBusy { get; private set; }

    // Build or reuse the db for the current source files and return its path, or null when the feature
    // is inactive — this service owns its whole activity decision (SplitTunneling.UseIpViaApp + premium plan).
    // The file name carries the source signature, so a changed source builds a NEW file and a running
    // VpnService can keep the superseded db open until it live-swaps to the returned path. Missing
    // files count as empty, and empty sources leave their sets empty (a no-op gate). Failures propagate
    // and fail to connect: a split the user configured is enforced or the connection does not proceed —
    // never silently skipped.
    internal async Task<string?> EnsureSplitIpDb(string dbFolder, CancellationToken cancellationToken)
    {
        // effective: the super toggle and the premium plan are both resolved here, so an inactive
        // feature is simply a false flag — no second gate to keep in sync
        if (!settingsService.UserSettings.SplitTunneling.ToEffective(premiumFeatureChecker).UseIpViaApp)
            return null;

        var settings = settingsService.SplitIpViaAppSettings;

        try {
            // set loading state
            IsBusy = true;
            StateChanged?.Invoke(this, EventArgs.Empty);

            // the signature is stat-only (modified time + length); the text files are parsed only on
            // rebuild. It is captured once so the file name and the stored meta describe the same source.
            var signature = settings.GetSignature();
            var dbBuilder = new IpRangeListDbBuilder(
                () => signature,
                includesFactory: () => ParseRanges(settings.Includes),
                excludesFactory: () => ParseRanges(settings.Excludes),
                blocksFactory: () => ParseRanges(settings.Blocks));

            var dbPath = Path.Combine(dbFolder, $"split-ip-via-app.{VhUtils.GetHexStringSha256(signature, 16)}.db");
            await dbBuilder.EnsureAsync(dbPath, cancellationToken).Vhc();
            return dbPath;
        }
        finally {
            IsBusy = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // invoked only on the rebuild path; each set stores exactly what the user wrote — an empty source
    // parses to an empty set (for includes that means "no constraint", not "nothing")
    private static IpRangeOrderedList ParseRanges(string text) =>
        (IpRangeTextFileParser.Parse(text) ?? []).ToOrderedList();
}
