
using VpnHood.AppLib.Api.SplitTunneling;
using VpnHood.AppLib.App.Utils;
using VpnHood.Net.Toolkit.Storages;

namespace VpnHood.AppLib.App.Settings;

// The split-domain source lists (see SplitTunneling.UseDomain). They feed the split-domain db, which
// live-applies to a running session through a reconfigure.
public class SplitDomainSettings(Storage storage)
    : SplitFileSettings(storage)
{
    // Raised by Set when the lists actually changed, AFTER the files are written — a handler that
    // re-reads the settings sees the new content. The owner (VpnHoodApp) reacts.
    public event EventHandler? Changed;

    public string Includes {
        get => Read("includes");
        set => Write("includes", value);
    }

    public string Excludes {
        get => Read("excludes");
        set => Write("excludes", value);
    }

    public string Blocks {
        get => Read("blocks");
        set => Write("blocks", value);
    }

    // The sources' change signature, stored as the db's source_signature meta so
    // SplitDomainDbBuilder.EnsureAsync detects stale dbs without parsing the lists.
    public string GetSignature() => GetSignature("includes", "excludes", "blocks");

    public SplitDomains Get() => new() {
        Includes = Includes,
        Excludes = Excludes,
        Blocks = Blocks
    };

    public void Set(SplitDomains value)
    {
        var changed = Includes != value.Includes || Excludes != value.Excludes || Blocks != value.Blocks;

        Includes = value.Includes;
        Excludes = value.Excludes;
        Blocks = value.Blocks;

        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    protected override void Validate(string content) => DomainTextFileParser.Parse(content);
}
