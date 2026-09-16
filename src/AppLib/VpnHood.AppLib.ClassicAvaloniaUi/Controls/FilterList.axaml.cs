using Avalonia.Controls;
using Avalonia.Interactivity;
using VpnHood.AppLib.Assets;
using VpnHood.AppLib.AvaloniaUI;
using VpnHood.AppLib.ClassicAvaloniaUi.Helpers;
using VpnHood.AppLib.ClassicAvaloniaUi.ViewModels;

namespace VpnHood.AppLib.ClassicAvaloniaUi.Controls;

public partial class FilterList : UserControl
{
    private IReadOnlyList<FilterItem> _items = [];

    // raised after a row, Select all or Clear all changed the selection; the page saves
    public event EventHandler? SelectionChanged;

    public FilterList()
    {
        InitializeComponent();
        // a remote types nothing until asked: the field is behind its button on a TV, in view elsewhere
        SearchBox.IsVisible = !AppData.IsTvUi;
        SearchButton.IsVisible = AppData.IsTvUi;
        SearchBox.PlaceholderText = Strings.Current.Search;
    }

    public IReadOnlyList<FilterItem> Items {
        get => _items;
        set {
            _items = value;
            ApplyFilter();
        }
    }

    // a flag is a small rectangle, an app icon a round avatar
    public bool IsIconFlag {
        get;
        set {
            field = value;
            List.Classes.Set("flags", value);
        }
    }

    public bool IsLoading {
        get => LoadingPanel.IsVisible;
        set {
            LoadingPanel.IsVisible = value;
            List.IsVisible = !value;
        }
    }

    // dims the list and blocks input, pointer and keys alike; the selection keeps showing
    public bool IsDisabled {
        get => !IsEnabled;
        set {
            IsEnabled = !value;
            Opacity = value ? 0.5 : 1;
        }
    }

    public Control? FirstRow => List.ItemCount > 0 ? List.ContainerFromIndex(0) : null;

    private void ApplyFilter()
    {
        var search = SearchBox.Text?.Trim();
        List.ItemsSource = string.IsNullOrEmpty(search)
            ? _items
            : [.. _items.Where(x => x.Name.Contains(search, StringComparison.OrdinalIgnoreCase))];
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void OnSearchClick(object? sender, RoutedEventArgs e)
    {
        SearchBox.IsVisible = true;
        SearchButton.IsVisible = false;
        SearchBox.LandFocus();
    }

    // Flipped in place: the row is what re-renders, and the switch answers the press at once.
    private void OnRowClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not FilterItem item)
            return;
        item.IsSelected = !item.IsSelected;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void OnSelectAllClick(object? sender, RoutedEventArgs e)
    {
        try {
            await SetAll(true, Strings.Current.SelectAllItems);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnClearAllClick(object? sender, RoutedEventArgs e)
    {
        try {
            await SetAll(false, Strings.Current.ClearAllItems);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async Task SetAll(bool isSelected, string title)
    {
        var host = this.FindHost();
        if (host == null || !await host.Confirm(title, Strings.Current.AreYouSure))
            return;

        foreach (var item in _items)
            item.IsSelected = isSelected;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
