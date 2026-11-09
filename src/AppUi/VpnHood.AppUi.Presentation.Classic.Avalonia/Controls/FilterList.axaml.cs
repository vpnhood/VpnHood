using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using VpnHood.AppUi.Common;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Helpers;
using VpnHood.AppUi.Presentation.Classic.Avalonia.ViewModels;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Controls;

public partial class FilterList : UserControl
{
    private IReadOnlyList<FilterItem> _items = [];

    // raised after a row, Select all or Clear all changed the selection; the page saves
    public event EventHandler? SelectionChanged;

    public FilterList()
    {
        InitializeComponent();
        // a remote types nothing until asked: the field is behind its button on a TV, in view elsewhere
        SearchBox.IsVisible = !VhApp.IsTvUi;
        SearchButton.IsVisible = VhApp.IsTvUi;
        SearchBox.PlaceholderText = Strings.Current.Search;
        // Up and Down walk the rows by their place in the list: XY focus finds only the rows built, and
        // the list builds only those in sight. At either end they fall through to XY focus, which walks
        // on to the page's other controls.
        List.AddHandler(KeyDownEvent, OnRowKeyDown, RoutingStrategies.Tunnel);
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

    // the row itself: the list's container around it takes no focus
    public Control? FirstRow => (List.ContainerFromIndex(0) as ContentPresenter)?.Child;

    private void ApplyFilter()
    {
        var search = SearchBox.Text?.Trim();
        List.ItemsSource = string.IsNullOrEmpty(search)
            ? _items
            : _items.Where(x => x.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private void OnRowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down) ||
            (e.Source as Visual)?.FindAncestorOfType<SwitchRow>(includeSelf: true) is not { } row ||
            row.GetVisualParent() is not Control container)
            return;

        var index = List.IndexFromContainer(container) + (e.Key == Key.Down ? 1 : -1);
        if (index < 0 || index >= List.ItemCount)
            return;

        List.ScrollIntoView(index);
        if ((List.ContainerFromIndex(index) as ContentPresenter)?.Child is not { } next)
            return;

        next.Focus(NavigationMethod.Directional);
        e.Handled = true;
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
