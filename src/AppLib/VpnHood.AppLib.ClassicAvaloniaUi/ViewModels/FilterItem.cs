using System.ComponentModel;
using Avalonia.Media.Imaging;

namespace VpnHood.AppLib.ClassicAvaloniaUi.ViewModels;

// One row of a filter list (the web UI's IListItemInfo): an app or a country, with its icon and
// whether it is in.
public sealed class FilterItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public required string Id { get; init; }
    public required string Name { get; init; }
    public required Bitmap? Icon { get; init; }

    public bool IsSelected {
        get;
        set {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}
