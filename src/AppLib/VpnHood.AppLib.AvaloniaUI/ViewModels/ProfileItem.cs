using System.ComponentModel;
using VpnHood.AppLib.AvaloniaUI.Resources;

namespace VpnHood.AppLib.AvaloniaUI.ViewModels;

// One server in the client's list - the web UI's ExpansionPanel: the mark saying whether the app is
// set to it, its name, its locations while it is open and the first of their flags while it is
// closed, and the support id and host it was built from. A server with a single location has
// nothing to open: choosing it connects, as it does there. The panel's menu - rename, diagnose,
// custom endpoint, remove - is the row's menu button, off the TV; on a TV it is a phone's job.
public sealed class ProfileItem : INotifyPropertyChanged
{
    // how many flags a closed server shows before "+n" (UiConstants.locationNumberOnCollapsedProfile)
    public const int CollapsedFlagCount = 8;

    public event PropertyChangedEventHandler? PropertyChanged;

    public required Guid ClientProfileId { get; init; }
    public required string Name { get; init; }
    public required bool IsActive { get; init; }
    public required bool IsSingleLocation { get; init; }
    public required bool IsBuiltIn { get; init; }
    public required string SupportIdText { get; init; }
    public required string HostName { get; init; }
    public required bool HasCustomEndpoint { get; init; }
    public required IReadOnlyList<CollapsedFlag> CollapsedFlags { get; init; }
    public required int MoreLocationCount { get; init; }
    public required IReadOnlyList<LocationGroup> Groups { get; init; }

    public bool HasLocations => !IsSingleLocation;
    public bool HasMoreLocations => MoreLocationCount > 0;
    public string MoreLocationsText => $"+{MoreLocationCount}";
    public string ExpandGlyph => IsExpanded ? Mdi.MinusCircleOutline : Mdi.PlusCircleOutline;
    public bool ShowCollapsedFlags => HasLocations && !IsExpanded;

    // The menu: not on the TV, where everything in it is a phone's job through pairing and a popup
    // is a poor fit for a D-pad. Its items follow the web UI's ExpansionPanel: a built-in server
    // keeps its name, a head that takes no keys removes none, the mock tools stay behind their
    // debug command, and Diagnose waits for the app to be able to.
    public bool ShowMenu => !AppData.IsTvUi;
    public bool CanRename => !IsBuiltIn;
    public bool CanRemove => AppData.Features.IsAddAccessKeySupported;
    public bool ShowStarlinkTools => AppData.IsStarlinkToolsEnabled;
    public bool CanDiagnose => AppData.State.CanDiagnose;

    // Open when the app is set to this server, or when it has one location and so nothing to
    // open - the state the web UI's panel is mounted in.
    public bool IsExpanded {
        get;
        set {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ExpandGlyph)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowCollapsedFlags)));
        }
    }

    public bool SameAs(ProfileItem other)
    {
        return ClientProfileId == other.ClientProfileId && Name == other.Name && IsActive == other.IsActive &&
               IsSingleLocation == other.IsSingleLocation && SupportIdText == other.SupportIdText &&
               HostName == other.HostName && HasCustomEndpoint == other.HasCustomEndpoint &&
               MoreLocationCount == other.MoreLocationCount && CollapsedFlags.SequenceEqual(other.CollapsedFlags) &&
               Groups.Count == other.Groups.Count && !Groups.Where((x, i) => !x.SameAs(other.Groups[i])).Any();
    }
}
