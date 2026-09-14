using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using VpnHood.AppLib.AvaloniaUI.Helpers;
using VpnHood.AppLib.AvaloniaUI.Resources;
using VpnHood.AppLib.AvaloniaUI.ViewModels;

namespace VpnHood.AppLib.AvaloniaUI.Views;

public partial class LocationsView : UserControl, IPage
{
    private readonly MainViewModel _viewModel;
    private readonly MainView _host;

    public LocationsView(MainViewModel viewModel, MainView host)
    {
        _viewModel = viewModel;
        _host = host;
        DataContext = viewModel;
        InitializeComponent();
        // a tap anywhere on a row, from any of the group lists
        AddHandler(TappedEvent, OnTapped);
        // The keys are taken on the way down, before the row and its list see them: the list moves
        // its own selection on Up and Down with a focus that lights no ring
        // (NavigationMethod.Unspecified), and the row takes Enter and Space for its selection and
        // marks them handled, so they never reached this view's OnKeyDown. Here Up and Down move
        // the focus as a remote's keys should - directionally, so the ring follows - and at either
        // end fall through to XY focus, which walks into the next card; Enter and Space choose the
        // row.
        AddHandler(KeyDownEvent, OnRowKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnRowKeyDown(object? sender, KeyEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(true) is not { } row ||
            row.FindAncestorOfType<ListBox>() is not { } list)
            return;

        if (e.Key is Key.Enter or Key.Space) {
            e.Handled = true;
            if (row.DataContext is LocationItem location)
                _ = Choose(location);
            return;
        }

        if (e.Key is not (Key.Up or Key.Down))
            return;

        var index = list.IndexFromContainer(row) + (e.Key == Key.Down ? 1 : -1);
        if (index < 0 || index >= list.ItemCount || list.ContainerFromIndex(index) is not Control next)
            return;

        list.SelectedIndex = index;
        next.Focus(NavigationMethod.Directional);
        e.Handled = true;
    }

    // The active location is where the input starts, so a press without a change is a no-op and
    // the nearest alternatives are one step away. Its row exists only once the lists have been laid
    // out, which may be after this call: then the landing waits for that layout. A client's list
    // may show no row at all - every server closed, or none added yet - and the input lands on the
    // first thing on the page instead, which is the way to add one.
    public void FocusDefault()
    {
        if (!FocusActiveRow())
            LayoutUpdated += OnFirstLayout;
    }

    private void OnFirstLayout(object? sender, EventArgs e)
    {
        LayoutUpdated -= OnFirstLayout;
        if (!FocusActiveRow())
            FocusFirstControl();
    }

    private void FocusFirstControl()
    {
        var first = Scroller.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(x => x is { IsVisible: true, IsEffectivelyEnabled: true, Focusable: true });
        if (first != null)
            first.LandFocus();
        else
            Header.FocusBack();
    }

    private bool FocusActiveRow()
    {
        var lists = this.GetVisualDescendants().OfType<ListBox>().Where(x => x.IsVisible).ToArray();
        foreach (var list in lists) {
            var active = (list.ItemsSource as IEnumerable<LocationItem>)?.FirstOrDefault(x => x.IsActive);
            if (active == null || list.ContainerFromItem(active) is not Control row)
                continue;
            list.SelectedItem = active;
            row.LandFocus();
            return true;
        }

        // no active row: the first row of the first list
        if (lists.FirstOrDefault()?.ContainerFromIndex(0) is not Control first)
            return false;
        first.LandFocus();
        return true;
    }

    // A tap chooses the row it lands on: a finger, a mouse.
    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (RowOf(e.Source) is { } location)
            _ = Choose(location);
    }

    // A server's row: it opens on its locations, unless it has a single one - then there is nothing
    // to open and choosing it connects, as the web UI's panel click does.
    private void OnProfileClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ProfileItem profile)
            return;

        if (!profile.IsSingleLocation) {
            profile.IsExpanded = !profile.IsExpanded;
            return;
        }

        _host.GoBack();
        _ = _viewModel.ConnectToProfile(profile.ClientProfileId);
    }

    // A vh:// key is a long base64 blob no remote can type, so a TV sends the person to their phone
    // - the page that hands the address out - and says so there; anything with a keyboard gets the
    // field. The web UI's onAddServer, with its dialog as our page.
    private void OnAddServerClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.IsTv)
            _host.Navigate(new PairingView(_host, Strings.Current.RemoteAccessHintServers));
        else
            _host.Navigate(new AddServerView(_viewModel, _host));
    }

    private static LocationItem? RowOf(object? source)
    {
        return (source as Visual)?.FindAncestorOfType<ListBoxItem>(true)?.DataContext as LocationItem;
    }

    private async Task Choose(LocationItem location)
    {
        _host.GoBack();
        // already there: the web UI says so in a snackbar and does not reconnect
        if (_viewModel.IsConnected && location.IsActive)
            return;
        await _viewModel.ConnectTo(location);
    }
}
