using VpnHood.AppLib.Settings;
using VpnHood.Core.Filtering.Sqlite;
using VpnHood.Core.Toolkit.Extensions;

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

    // Build or reuse the db for the current split-domain files. The UseSplitDomain gate lives in the caller
    // (VpnHoodApp.PrepareSplitDomainDbs); missing files count as empty, and empty sources leave their sets
    // empty (a no-op gate). Failures propagate and fail to connect: a split the user configured is enforced
    // or the connection does not proceed — never silently skipped.
    public async Task EnsureSplitDomainDb(string dbPath, CancellationToken cancellationToken)
    {
        var splitDomainSettings = settingsService.SplitDomainSettings;

        try {
            // set loading state
            IsBusy = true;
            StateChanged?.Invoke(this, EventArgs.Empty);

            // the signature is stat-only (modified time + length); the text files are parsed only on rebuild
            var dbBuilder = new DomainListDbBuilder(
                splitDomainSettings.GetSplitDomainSignature,
                includesFactory: () => DomainTextFileParser.Parse(splitDomainSettings.Includes) ?? [],
                excludesFactory: () => DomainTextFileParser.Parse(splitDomainSettings.Excludes) ?? [],
                blocksFactory: () => DomainTextFileParser.Parse(splitDomainSettings.Blocks) ?? []);
            await dbBuilder.EnsureAsync(dbPath, cancellationToken).Vhc();
        }
        finally {
            IsBusy = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
