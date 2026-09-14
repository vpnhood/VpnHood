using System.ComponentModel;

namespace VpnHood.AppLib.AvaloniaUI.ViewModels;

// A card of the location list - the web UI's LocationGroup: Free or Premium when the list is split,
// or the one untitled card when it is not. Open by default, as the web UI opens both.
public sealed class LocationGroup : INotifyPropertyChanged
{
    private bool _isExpanded = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public required string Title { get; init; }
    public required bool IsPremium { get; init; }
    public required IReadOnlyList<LocationItem> Items { get; init; }
    public bool HasTitle => Title.Length > 0;

    public bool IsExpanded {
        get => _isExpanded;
        set {
            if (_isExpanded == value) return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    public bool SameAs(LocationGroup other)
    {
        return Title == other.Title && IsPremium == other.IsPremium && Items.SequenceEqual(other.Items);
    }
}
