using System.ComponentModel;
using VpnHood.AppLib.AvaloniaUI.Resources;

namespace VpnHood.AppLib.AvaloniaUI.ViewModels;

// One server in the client's list - the web UI's ExpansionPanel: the mark saying whether the app is
// set to it, its name, its locations while it is open, and the support id and host it was built
// from. A server with a single location has nothing to open: choosing it connects, as it does
// there. The panel's menu - rename, diagnose, custom endpoint, remove - is not here, for the reason
// the web UI hides it on a TV: it is a phone's job, over remote access.
public sealed class ProfileItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public required Guid ClientProfileId { get; init; }
    public required string Name { get; init; }
    public required bool IsActive { get; init; }
    public required bool IsSingleLocation { get; init; }
    public required string SupportIdText { get; init; }
    public required string HostName { get; init; }
    public required IReadOnlyList<LocationGroup> Groups { get; init; }

    public bool HasLocations => !IsSingleLocation;
    public string ExpandGlyph => IsExpanded ? Mdi.MinusCircleOutline : Mdi.PlusCircleOutline;

    // Open when the app is set to this server, or when it has one location and so nothing to
    // open - the state the web UI's panel is mounted in.
    public bool IsExpanded {
        get;
        set {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ExpandGlyph)));
        }
    }

    public bool SameAs(ProfileItem other)
    {
        return ClientProfileId == other.ClientProfileId && Name == other.Name && IsActive == other.IsActive &&
               IsSingleLocation == other.IsSingleLocation && SupportIdText == other.SupportIdText &&
               HostName == other.HostName && Groups.Count == other.Groups.Count &&
               !Groups.Where((x, i) => !x.SameAs(other.Groups[i])).Any();
    }
}
