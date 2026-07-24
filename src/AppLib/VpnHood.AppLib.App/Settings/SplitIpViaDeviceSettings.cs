using VpnHood.AppLib.Dtos;

namespace VpnHood.AppLib.Settings;

// The split-ip-via-device source lists (see UseSplitIpViaDevice). They shape the vpn adapter's
// ranges, which are applied at connect only — a change while connected flags the session for a
// reconnect instead of live-applying.
public class SplitIpViaDeviceSettings(string folderPath) : SplitFileSettings(folderPath)
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

    public SplitIpsViaDevice Get() => new() {
        Includes = Includes,
        Excludes = Excludes
    };

    public void Set(SplitIpsViaDevice value)
    {
        var changed = Includes != value.Includes || Excludes != value.Excludes;

        Includes = value.Includes;
        Excludes = value.Excludes;

        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    protected override void Validate(string content) => IpRangeTextFileParser.Parse(content);
}
