using System.ComponentModel;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.ViewModels;

// A card of the location list: Free or Premium when the list is split,
// or the one untitled card when it is not. Open by default.
public sealed class LocationGroup : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public required string Title { get; init; }
    public required bool IsPremium { get; init; }
    public required IReadOnlyList<LocationItem> Items { get; init; }
    public bool HasTitle => Title.Length > 0;

    // A card of a server's card, in the client's list of servers, rather than a card of the page:
    // it takes the darker of the two.
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
