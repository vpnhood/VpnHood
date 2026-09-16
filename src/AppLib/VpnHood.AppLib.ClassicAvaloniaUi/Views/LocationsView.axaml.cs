using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using VpnHood.AppLib.Assets;
using VpnHood.AppLib.AvaloniaUI;
using VpnHood.AppLib.ClassicAvaloniaUi.Helpers;
using VpnHood.AppLib.ClassicAvaloniaUi.ViewModels;
using VpnHood.AppLib.ClassicAvaloniaUi.Views.Dialogs;
using VpnHood.AppLib.Contracts.ClientProfiles;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.ClassicAvaloniaUi.Views;

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
        if (index < 0 || index >= list.ItemCount || list.ContainerFromIndex(index) is not { } next)
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
            if (active == null || list.ContainerFromItem(active) is not { } row)
                continue;
            list.SelectedItem = active;
            row.LandFocus();
            return true;
        }

        // no active row: the first row of the first list
        if (lists.FirstOrDefault()?.ContainerFromIndex(0) is not { } first)
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

    // ---- the server's menu (ExpansionPanel.vue), off the TV ----

    // The server whose menu is open: taken from the menu button, which sits in the server's own
    // template, rather than from the items, which sit in a popup of their own - a popup whose
    // content loses its DataContext the moment it closes, which is the first thing an item does.
    private ProfileItem? _menuProfile;

    private ProfileItem? ProfileOf(object? sender)
    {
        return (sender as Control)?.DataContext as ProfileItem ?? _menuProfile;
    }

    // the menu button sits inside the header button: its press is its own, not the header's
    private void OnMenuClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _menuProfile = (sender as Control)?.DataContext as ProfileItem;
        (sender as Button)?.FocusFirstMenuItem();
    }

    private void OnCollapsedClick(object? sender, RoutedEventArgs e)
    {
        if (ProfileOf(sender) is { } profile)
            profile.IsExpanded = true;
    }

    // a menu item's popup closes when the item is chosen
    private static void CloseMenu(object? sender)
    {
        if ((sender as Control)?.FindLogicalAncestorOfType<Popup>() is { } popup)
            popup.IsOpen = false;
    }

    private async void OnRenameClick(object? sender, RoutedEventArgs e)
    {
        try {
            CloseMenu(sender);
            if (ProfileOf(sender) is not { } profile)
                return;
            var dialog = new RenameServerDialog(profile.Name);
            if (!await _host.ShowDialog(dialog))
                return;
            try {
                // an empty name gives the server its default name back (SAVE_EMPTY_TO_DISPLAY_DEFAULT_NAME)
                var name = string.IsNullOrWhiteSpace(dialog.NewName) ? null : dialog.NewName.Trim();
                await AppData.Api.ClientProfiles.Update(profile.ClientProfileId, new ClientProfileUpdateParams {
                    ClientProfileName = new Patch<string?>(name)
                }, CancellationToken.None);
                await _viewModel.ReloadConfig();
            }
            catch (Exception ex) {
                await _host.ProcessError(ex);
            }
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnDiagnoseClick(object? sender, RoutedEventArgs e)
    {
        try {
            CloseMenu(sender);
            if (ProfileOf(sender) is not { } profile)
                return;
            _host.GoBack();
            await _viewModel.ConnectWithProfile(profile.ClientProfileId, isDiagnose: true);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnCustomEndpointClick(object? sender, RoutedEventArgs e)
    {
        try {
            CloseMenu(sender);
            if (ProfileOf(sender) is not { } profile || AppData.FindClientProfileInfo(profile.ClientProfileId) is not { } info)
                return;
            if (await _host.ShowDialog(new CustomEndpointDialog(_host, info)))
                await _viewModel.ReloadConfig();
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private void OnStarlinkClick(object? sender, RoutedEventArgs e)
    {
        CloseMenu(sender);
        if (ProfileOf(sender) is { } profile)
            _host.Navigate(new StarlinkToolsView(_host, profile.ClientProfileId));
    }

    // removing the server the app is connected through disconnects first (ClientProfileController.Delete)
    private async void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        try {
            CloseMenu(sender);
            if (ProfileOf(sender) is not { } profile)
                return;
            var s = Strings.Current;
            if (!await _host.Confirm(s.Warning, $"{s.ConfirmRemoveServer}\n\n{profile.Name}"))
                return;
            try {
                await AppData.Api.ClientProfiles.Delete(profile.ClientProfileId, CancellationToken.None);
                await _viewModel.ReloadConfig();
            }
            catch (Exception ex) {
                await _host.ProcessError(ex);
            }
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
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
