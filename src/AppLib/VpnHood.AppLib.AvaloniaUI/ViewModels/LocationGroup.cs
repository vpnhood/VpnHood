using System.ComponentModel;

namespace VpnHood.AppLib.AvaloniaUI.ViewModels;

// A card of the location list - the web UI's LocationGroup: Free or Premium when the list is split,
// or the one untitled card when it is not. Open by default, as the web UI opens both.
public sealed class LocationGroup : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public required string Title { get; init; }
    public required bool IsPremium { get; init; }
    public required IReadOnlyList<LocationItem> Items { get; init; }
    public bool HasTitle => Title.Length > 0;

    // A card of a server's card, in the client's list of servers, rather than a card of the page:
    // the web UI gives it the darker of the two (LocationGroup.vue's expansion-panels-collapsed).
    public bool IsNested { get; init; }

    public bool IsExpanded {
        get;
        set {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    } = true;

    public bool SameAs(LocationGroup other)
    {
        return Title == other.Title && IsPremium == other.IsPremium && IsNested == other.IsNested &&
               Items.SequenceEqual(other.Items);
    }
}
