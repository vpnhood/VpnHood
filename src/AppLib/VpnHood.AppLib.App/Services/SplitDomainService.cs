using VpnHood.AppLib.Settings;
using VpnHood.Core.Filtering.Sqlite;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Services;

// Prepares the on-disk split-domain filter db before connecting — ONE self-describing db whose sets
// mirror the user's three source files exactly (no merge algebra, each table answers "what did the user
// write"):
//   include: force these domains through the tunnel, past any ip-gate veto (the Include override lane)
//   exclude: members bypass the tunnel
//   block:   members are dropped entirely at app level
public class SplitDomainService(AppSettingsService settingsService)
{
    public event EventHandler? StateChanged;

    public bool IsBusy { get; private set; }

    // Build or reuse the db for the current split-domain files and return its path. The file name carries
    // the source signature, so a changed source builds a NEW file and a running VpnService can keep the
    // superseded db open until it live-swaps to the returned path. The UseSplitDomain gate lives in the
    // caller (VpnHoodApp.PrepareSplitDomainDbs); missing files count as empty, and empty sources leave
    // their sets empty (a no-op gate). Failures propagate and fail to connect: a split the user configured
    // is enforced or the connection does not proceed — never silently skipped.
    public async Task<string> EnsureSplitDomainDb(string dbFolder, CancellationToken cancellationToken)
    {
        var splitDomainSettings = settingsService.SplitDomainSettings;

        try {
            // set loading state
            IsBusy = true;
            StateChanged?.Invoke(this, EventArgs.Empty);

            // the signature is stat-only (modified time + length); the text files are parsed only on
            // rebuild. It is captured once so the file name and the stored meta describe the same source.
            var signature = splitDomainSettings.GetSplitDomainSignature();
            var dbBuilder = new DomainListDbBuilder(
                () => signature,
                includesFactory: () => DomainTextFileParser.Parse(splitDomainSettings.Includes) ?? [],
                excludesFactory: () => DomainTextFileParser.Parse(splitDomainSettings.Excludes) ?? [],
                blocksFactory: () => DomainTextFileParser.Parse(splitDomainSettings.Blocks) ?? []);

            var dbPath = Path.Combine(dbFolder, $"split-domain.{VhUtils.GetHexStringSha256(signature, 16)}.db");
            await dbBuilder.EnsureAsync(dbPath, cancellationToken).Vhc();
            return dbPath;
        }
        finally {
            IsBusy = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
