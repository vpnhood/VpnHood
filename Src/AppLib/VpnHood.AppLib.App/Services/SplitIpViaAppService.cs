using VpnHood.AppLib.Settings;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.Sqlite;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Services;

// Prepares the on-disk split-ip-via-app filter dbs before connecting — two sibling dbs:
//   split-ip-via-app.db        the include/exclude files merged into ONE range list
//                              (All ∩ includes − excludes), Include action: members are tunnel-eligible,
//                              non-members bypass. Unlike split-country there is no smaller-set inversion:
//                              inverting an arbitrary range list yields about the same row count.
//   split-ip-via-app-blocks.db the AppBlocks set, Block action: members are dropped entirely at app level.
public class SplitIpViaAppService(AppSettingsService settingsService)
{
    public event EventHandler? StateChanged;

    public bool IsBusy { get; private set; }

    // Build or reuse the dbs for the current app filter files and return their descriptors. The
    // UseSplitIpViaApp gate lives in the caller (VpnHoodApp.PrepareSplitIpDbs); missing files count as
    // empty, and empty sources build no-op dbs (All for include ⇒ everything tunnel-eligible; None for
    // blocks ⇒ nothing dropped). Failures propagate and fail the connect: a split the user configured is
    // enforced or the connection does not proceed — never silently skipped.
    public async Task<SplitIpDbFilter[]> EnsureSplitIpDbs(string dbFolder, CancellationToken cancellationToken)
    {
        var splitIpSettings = settingsService.SplitIpSettings;

        try {
            // set loading state
            IsBusy = true;
            StateChanged?.Invoke(this, EventArgs.Empty);

            // the signatures are stat-only (mtime + length); the text files are parsed only on rebuild
            var includeDbPath = Path.Combine(dbFolder, "split-ip-via-app.db");
            var includeDbBuilder = new IpRangeListDbBuilder(BuildIncludeIpRanges, splitIpSettings.GetAppFilterSignature);
            await includeDbBuilder.EnsureAsync(includeDbPath, cancellationToken).Vhc();

            var blockDbPath = Path.Combine(dbFolder, "split-ip-via-app-blocks.db");
            var blockDbBuilder = new IpRangeListDbBuilder(BuildBlockIpRanges, splitIpSettings.GetAppBlocksSignature);
            await blockDbBuilder.EnsureAsync(blockDbPath, cancellationToken).Vhc();

            return [
                new SplitIpDbFilter { DbPath = includeDbPath, Action = FilterAction.Include },
                new SplitIpDbFilter { DbPath = blockDbPath, Action = FilterAction.Block }
            ];
        }
        finally {
            IsBusy = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // invoked only on the rebuild path; merges the include/exclude sources into the single stored set
    private IpRangeOrderedList BuildIncludeIpRanges()
    {
        var splitIpSettings = settingsService.SplitIpSettings;
        return IpNetwork.All.ToIpRanges()
            .Intersect(IpRangeTextFileParser.ParseIncludes(splitIpSettings.AppIncludes))
            .Exclude(IpRangeTextFileParser.ParseExcludes(splitIpSettings.AppExcludes));
    }

    // invoked only on the rebuild path; empty blocks parse to None (an empty, no-op db)
    private IpRangeOrderedList BuildBlockIpRanges()
    {
        return IpRangeTextFileParser.ParseExcludes(settingsService.SplitIpSettings.AppBlocks).ToOrderedList();
    }
}
