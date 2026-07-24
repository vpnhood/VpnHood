using VpnHood.AppLib.Settings;
using VpnHood.Core.Filtering.Sqlite;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Services;

// Prepares the on-disk split-ip-via-app filter db before connecting — ONE self-describing db whose sets
// mirror the user's three source files exactly (no merge algebra, each table answers "what did the user
// write"):
//   include: tunnel only members — a non-empty set vetoes non-members; empty ⇒ no constraint
//   exclude: members bypass the tunnel
//   block:   members are dropped entirely at app level
public class SplitIpViaAppService(AppSettingsService settingsService)
{
    public event EventHandler? StateChanged;

    public bool IsBusy { get; private set; }

    // Build or reuse the db for the current app filter files and return its path. The file name carries
    // the source signature, so a changed source builds a NEW file and a running VpnService can keep the
    // superseded db open until it live-swaps to the returned path. The UseSplitIpViaApp gate lives in the
    // caller (VpnHoodApp.PrepareSplitIpDbs); missing files count as empty, and empty sources leave their
    // sets empty (a no-op gate). Failures propagate and fail to connect: a split the user configured is
    // enforced or the connection does not proceed — never silently skipped.
    public async Task<string> EnsureSplitIpDb(string dbFolder, CancellationToken cancellationToken)
    {
        var splitIpSettings = settingsService.SplitIpSettings;

        try {
            // set loading state
            IsBusy = true;
            StateChanged?.Invoke(this, EventArgs.Empty);

            // the signature is stat-only (modified time + length); the text files are parsed only on
            // rebuild. It is captured once so the file name and the stored meta describe the same source.
            var signature = splitIpSettings.GetSplitIpViaAppSignature();
            var dbBuilder = new IpRangeListDbBuilder(
                () => signature,
                includesFactory: () => ParseRanges(splitIpSettings.AppIncludes),
                excludesFactory: () => ParseRanges(splitIpSettings.AppExcludes),
                blocksFactory: () => ParseRanges(splitIpSettings.AppBlocks));

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
