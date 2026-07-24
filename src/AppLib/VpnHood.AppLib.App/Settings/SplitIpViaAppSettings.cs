using VpnHood.AppLib.Dtos;
using VpnHood.AppLib.Utils;

namespace VpnHood.AppLib.Settings;

// The split-ip-via-app source lists (see UseSplitIpViaApp). They feed the split-ip db, which
// live-applies to a running session through a reconfigure.
public class SplitIpViaAppSettings(string folderPath) : SplitFileSettings(folderPath)
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

    // Stat-only change signature of the sources, stored as the db's source_signature meta so
    // SplitIpDbBuilder.EnsureAsync detects stale dbs without parsing the text files. Every setting
    // write rewrites its file, so the signature always changes with the content.
    public string GetSignature() => AppUtils.BuildFileSignature(
        GetFilePath("includes"),
        GetFilePath("excludes"),
        GetFilePath("blocks"));

    public SplitIpsViaApp Get() => new() {
        Includes = Includes,
        Excludes = Excludes,
        Blocks = Blocks
    };

    public void Set(SplitIpsViaApp value)
    {
        var changed = Includes != value.Includes || Excludes != value.Excludes || Blocks != value.Blocks;

        Includes = value.Includes;
        Excludes = value.Excludes;
        Blocks = value.Blocks;

        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    protected override void Validate(string content) => IpRangeTextFileParser.Parse(content);
}
