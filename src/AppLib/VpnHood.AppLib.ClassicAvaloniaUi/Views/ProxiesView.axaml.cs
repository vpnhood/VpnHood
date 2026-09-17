using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Assets;
using VpnHood.AppLib.AvaloniaUI;
using VpnHood.AppLib.ClassicAvaloniaUi.Controls;
using VpnHood.AppLib.ClassicAvaloniaUi.Helpers;
using VpnHood.AppLib.ClassicAvaloniaUi.Views.Dialogs;
using VpnHood.AppLib.Api.Proxies;
using VpnHood.AppLib.Api.Settings;

namespace VpnHood.AppLib.ClassicAvaloniaUi.Views;

public partial class ProxiesView : UserControl, IPage, IDisposable
{
    private const int ItemsPerPage = 10;
    private const string AutoRefreshKey = "enableAutoRefreshProxyList";
    private static readonly TimeSpan AutoRefreshPeriod = TimeSpan.FromSeconds(3);

    private readonly MainView _host;
    private readonly DispatcherTimer _refreshTimer;
    private IReadOnlyList<AppProxyEndPointInfo> _proxies = [];
    private int _totalCount;
    private int _page = 1;
    private string? _filter;
    private string? _oldUrl;
    private bool _isLoading;
    private bool _isReloadingUrl;
    private bool _isBusy;
    private bool _disposed;

    private static AppProxySettings ProxySettings => AppModel.UserSettings.ProxySettings;

    public ProxiesView(MainView host)
    {
        _host = host;
        InitializeComponent();
        var s = Strings.Current;
        _refreshTimer = new DispatcherTimer(AutoRefreshPeriod, DispatcherPriority.Background, (_, _) => _ = LoadProxies(showLoading: false));

        // the mode
        foreach (var (mode, title) in new[] { (AppProxyMode.NoProxy, s.NoProxy), (AppProxyMode.Device, s.System), (AppProxyMode.Manual, s.Manual) })
            ModeMenu.Children.Add(MenuItem(title, () => ChooseMode(mode)));

        // AddByUrl's schedule
        foreach (var (interval, title) in IntervalOptions())
            IntervalMenu.Children.Add(MenuItem(title, () => ChooseInterval(interval)));

        // the list's menu
        ListMenu.Children.Add(MenuItem(s.AutoRefresh, Mdi.RefreshAuto, ToggleAutoRefresh));
        ListMenu.Children.Add(MenuItem(s.ProxyResetStates, Mdi.Refresh, () => RunListAction(() => AppModel.Api.ProxyEndPoints.ResetStates(CancellationToken.None))));
        ListMenu.Children.Add(MenuItem(s.DisableAllFailed, Mdi.Cancel, () => RunListAction(() => AppModel.Api.ProxyEndPoints.DisableAllFailed(CancellationToken.None), s.DisableAllFailed, s.DisableAllFailedProxiesMsg)));
        ListMenu.Children.Add(MenuItem(s.RemoveAllFailed, Mdi.DeleteAlert,
            () => RunListAction(() => AppModel.Api.ProxyEndPoints.DeleteAll(deleteSucceeded: false, deleteFailed: true, deleteUnknown: false, deleteDisabled: false, CancellationToken.None), s.RemoveAllFailed, s.RemoveAllFailedMsg)));
        ListMenu.Children.Add(MenuItem(s.RemoveAllDisabled, Mdi.DeleteForever,
            () => RunListAction(() => AppModel.Api.ProxyEndPoints.DeleteAll(deleteSucceeded: false, deleteFailed: false, deleteUnknown: false, deleteDisabled: true, CancellationToken.None), s.RemoveAllDisabled, s.RemoveAllDisabledMsg)));
        ListMenu.Children.Add(MenuItem(s.RemoveAll, Mdi.Delete,
            () => RunListAction(() => AppModel.Api.ProxyEndPoints.DeleteAll(deleteSucceeded: true, deleteFailed: true, deleteUnknown: true, deleteDisabled: true, CancellationToken.None), s.RemoveAll, s.RemoveAllProxiesMsg), isError: true));

        // the filter
        FilterMenu.Children.Add(MenuItem(s.All, () => ChooseFilter(null)));
        FilterMenu.Children.Add(MenuItem(s.Succeeded, () => ChooseFilter("succeeded")));
        FilterMenu.Children.Add(MenuItem(s.Failed, () => ChooseFilter("failed")));
        FilterMenu.Children.Add(MenuItem(s.Disabled, () => ChooseFilter("disabled")));

        UrlBox.PlaceholderText = s.ProxyAutoUpdateUrlPlaceholder;
        ShowMode();
    }

    private static IReadOnlyList<(TimeSpan? Interval, string Title)> IntervalOptions()
    {
        var s = Strings.Current;
        return [
            (null, s.Never),
            (TimeSpan.FromMinutes(1), $"1 {s.Minute}"), (TimeSpan.FromMinutes(2), $"2 {s.Minutes}"), (TimeSpan.FromMinutes(3), $"3 {s.Minutes}"),
            (TimeSpan.FromMinutes(5), $"5 {s.Minutes}"), (TimeSpan.FromMinutes(10), $"10 {s.Minutes}"), (TimeSpan.FromMinutes(30), $"30 {s.Minutes}"),
            (TimeSpan.FromHours(1), $"1 {s.Hour}"), (TimeSpan.FromHours(2), $"2 {s.Hours}"), (TimeSpan.FromHours(4), $"4 {s.Hours}"),
            (TimeSpan.FromHours(5), $"5 {s.Hours}"), (TimeSpan.FromHours(12), $"12 {s.Hours}"), (TimeSpan.FromHours(24), $"24 {s.Hours}")
        ];
    }

    private Button MenuItem(string title, Action action)
    {
        var item = new Button { Content = title };
        item.Classes.Add("menu-item");
        item.Click += (_, _) => {
            CloseFlyouts();
            action();
        };
        return item;
    }

    private Button MenuItem(string title, string glyph, Action action, bool isError = false)
    {
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 10 };
        var icon = new TextBlock { Text = glyph, FontSize = 18, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        icon.Classes.Add("mdi");
        if (isError) icon.Classes.Add("error");
        var text = new TextBlock { Text = title, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        if (isError) text.Classes.Add("error");
        row.Children.Add(icon);
        row.Children.Add(text);
        var item = new Button { Content = row };
        item.Classes.Add("menu-item");
        item.Click += (_, _) => {
            CloseFlyouts();
            action();
        };
        return item;
    }

    private void OnMenuButtonClick(object? sender, RoutedEventArgs e)
    {
        (sender as Button)?.FocusFirstMenuItem();
    }

    private void CloseFlyouts()
    {
        ModeButton.Flyout?.Hide();
        IntervalButton.Flyout?.Hide();
        MenuButton.Flyout?.Hide();
        FilterButton.Flyout?.Hide();
    }

    public void FocusDefault()
    {
        ModeButton.LandFocus();
    }

    // ---- the mode ----

    private void ShowMode()
    {
        var s = Strings.Current;
        var mode = ProxySettings.Mode;
        ModeText.Text = mode switch { AppProxyMode.Device => s.System, AppProxyMode.Manual => s.Manual, _ => s.NoProxy };
        ProxyImage.MaxHeight = mode == AppProxyMode.Manual ? 130 : 280;
        DevicePanel.IsVisible = mode == AppProxyMode.Device;
        ManualPanel.IsVisible = mode == AppProxyMode.Manual;
        if (mode == AppProxyMode.Device)
            _ = ShowDeviceProxy();
        if (mode == AppProxyMode.Manual) {
            LoadAutoUpdateSettings();
            _ = LoadProxies(showLoading: true);
            StartPeriodicRefresh();
        }
        else {
            StopPeriodicRefresh();
        }
    }

    private async void ChooseMode(AppProxyMode mode)
    {
        try {
            var settings = AppModel.UserSettings;
            var previous = settings.ProxySettings.Mode;
            if (previous == mode)
                return;
            settings.ProxySettings.Mode = mode;
            try {
                await AppModel.SaveUserSettings(settings, CancellationToken.None);
            }
            catch (Exception ex) {
                settings.ProxySettings.Mode = previous;
                await _host.ProcessError(ex);
                return;
            }
            ShowMode();
            _host.ViewModel.Refresh();
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    // DeviceProxy.vue: the device's proxy, read once on arrival
    private async Task ShowDeviceProxy()
    {
        try {
            DeviceList.Children.Clear();
            var proxy = await AppModel.Api.ProxyEndPoints.GetDevice(CancellationToken.None);
            NoDeviceProxy.IsVisible = proxy == null;
            if (proxy != null)
                DeviceList.Children.Add(new ProxyListItem(proxy, isLast: true));
        }
        catch (Exception ex) {
            await _host.ProcessError(ex);
        }
    }

    // ---- AddByUrl ----

    private void LoadAutoUpdateSettings()
    {
        var options = ProxySettings.AutoUpdateOptions;
        UrlBox.Text = options.Url?.AbsoluteUri;
        _oldUrl = UrlBox.Text;
        UrlSwitch.IsChecked = options.Url != null;
        UrlPanel.IsVisible = options.Url != null;
        ShowInterval();
        ShowUrlState();
    }

    private void ShowInterval()
    {
        var interval = ProxySettings.AutoUpdateOptions.Interval;
        IntervalText.Text = IntervalOptions().FirstOrDefault(x => x.Interval == interval).Title ?? Strings.Current.Never;
    }

    private bool HasUrl => !string.IsNullOrWhiteSpace(UrlBox.Text);

    private void ShowUrlState()
    {
        ReloadButton.IsEnabled = HasUrl && !_isReloadingUrl;
        IntervalButton.IsEnabled = HasUrl;
        AutoUpdateLabel.Opacity = HasUrl ? 1 : 0.5;
    }

    // turning the switch off forgets the address and its schedule
    private async void OnUrlSwitchClick(object? sender, RoutedEventArgs e)
    {
        try {
            var isOn = UrlSwitch.IsChecked != true;
            UrlSwitch.IsChecked = isOn;
            UrlPanel.IsVisible = isOn;
            if (isOn)
                return;
            UrlBox.Text = null;
            ProxySettings.AutoUpdateOptions.Interval = null;
            await SaveAutoUpdateSettings();
            ShowInterval();
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private void OnUrlChanged(object? sender, TextChangedEventArgs e)
    {
        ShowUrlState();
    }

    private async void OnUrlLostFocus(object? sender, RoutedEventArgs e)
    {
        try {
            if (!HasUrl || UrlBox.Text == _oldUrl || _isReloadingUrl)
                return;
            await ReloadFromUrl();
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnReloadClick(object? sender, RoutedEventArgs e)
    {
        try {
            await ReloadFromUrl();
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async Task SaveAutoUpdateSettings()
    {
        var settings = AppModel.UserSettings;
        var text = UrlBox.Text?.Trim();
        settings.ProxySettings.AutoUpdateOptions.Url = string.IsNullOrEmpty(text) ? null : new Uri(text);
        await AppModel.SaveUserSettings(settings, CancellationToken.None);
    }

    private async Task ReloadFromUrl()
    {
        _isReloadingUrl = true;
        ReloadIcon.Classes.Set("spinner", true);
        ShowUrlState();
        try {
            await SaveAutoUpdateSettings();
            await AppModel.Api.ProxyEndPoints.ReloadUrl(CancellationToken.None);
            _oldUrl = UrlBox.Text;
            _page = 1;
            await LoadProxies(showLoading: true);
        }
        catch (Exception ex) {
            await _host.ProcessError(ex);
        }
        finally {
            _isReloadingUrl = false;
            ReloadIcon.Classes.Set("spinner", false);
            ShowUrlState();
        }
    }

    private async void ChooseInterval(TimeSpan? interval)
    {
        try {
            ProxySettings.AutoUpdateOptions.Interval = interval;
            await SaveAutoUpdateSettings();
            ShowInterval();
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    // ---- SavedProxies ----

    private async Task LoadProxies(bool showLoading)
    {
        if (_disposed)
            return;
        var filter = _filter;
        try {
            _isLoading = true;
            if (showLoading) {
                LoadingPanel.IsVisible = true;
                ProxyList.IsVisible = false;
            }
            UpdateButtons();
            var result = await AppModel.Api.ProxyEndPoints.List(
                search: null,
                includeSucceeded: filter is null or "succeeded",
                includeFailed: filter is null or "failed",
                includeUnknown: filter is null,
                includeDisabled: filter is null or "disabled",
                recordIndex: (_page - 1) * ItemsPerPage,
                recordCount: ItemsPerPage,
                cancellationToken: CancellationToken.None);
            _proxies = result.Items;
            _totalCount = result.TotalCount;
            ShowProxies();
        }
        catch (Exception ex) {
            await _host.ProcessError(ex);
        }
        finally {
            _isLoading = false;
            LoadingPanel.IsVisible = false;
            ProxyList.IsVisible = true;
            UpdateButtons();
        }
    }

    private void ShowProxies()
    {
        var s = Strings.Current;
        ProxyList.Children.Clear();
        for (var i = 0; i < _proxies.Count; i++) {
            var item = new ProxyListItem(_proxies[i], isLast: i == _proxies.Count - 1);
            item.Clicked += OnProxyClick;
            ProxyList.Children.Add(item);
        }
        EmptyText.IsVisible = _proxies.Count == 0;
        EmptyText.Text = _filter != null ? s.ProxyFilterNoResult : s.ProxyListEmpty;
        MenuButton.IsVisible = _proxies.Count > 0;
        FilterButton.IsVisible = _filter != null || _totalCount > ItemsPerPage;
        FilterText.Text = _filter switch { "succeeded" => s.Succeeded, "failed" => s.Failed, "disabled" => s.Disabled, _ => "Filter Status" };

        var totalPages = Math.Max(1, (_totalCount + ItemsPerPage - 1) / ItemsPerPage);
        Pagination.IsVisible = _totalCount > ItemsPerPage;
        PageText.Text = $"{_page} / {totalPages}";
        FirstPageButton.IsEnabled = _page > 1;
        PrevPageButton.IsEnabled = _page > 1;
        NextPageButton.IsEnabled = _page < totalPages;
        LastPageButton.IsEnabled = _page < totalPages;
        var start = _totalCount == 0 ? 0 : (_page - 1) * ItemsPerPage + 1;
        var end = Math.Min(_page * ItemsPerPage, _totalCount);
        PaginationText.Text = s.PaginationStatus(start, end, _totalCount);

        // ConnectionStatistics: worth a card once there is a list to speak of
        var stats = AppModel.State.ProxyConnectorStatus;
        StatsCard.IsVisible = _proxies.Count > 5 && stats != null;
        if (stats != null) {
            RecentSucceeded.Text = stats.SessionStatus.SucceededCount.ToString();
            RecentFailed.Text = stats.SessionStatus.FailedCount.ToString();
            ServersLabel.Text = $"{s.Servers} ({_totalCount})";
            ServersSucceeded.Text = stats.SucceededServerCount.ToString();
            ServersFailed.Text = stats.FailedServerCount.ToString();
            ServersDisabled.Text = stats.DisabledServerCount.ToString();
        }
    }

    private void UpdateButtons()
    {
        var isDisabled = _isBusy || _isLoading;
        MenuButton.IsEnabled = !isDisabled;
        AddButton.IsEnabled = !isDisabled;
        AddListButton.IsEnabled = !isDisabled;
        FilterButton.IsEnabled = !isDisabled;
    }

    private async Task RefreshList()
    {
        _page = 1;
        await LoadProxies(showLoading: true);
    }

    // a menu action, with its question first when it destroys something
    private async void RunListAction(Func<Task> action, string? confirmTitle = null, string? confirmMessage = null)
    {
        try {
            if (confirmTitle != null && confirmMessage != null && !await _host.Confirm(confirmTitle, confirmMessage))
                return;
            _isBusy = true;
            UpdateButtons();
            await action();
            await RefreshList();
        }
        catch (Exception ex) {
            await _host.ProcessError(ex);
        }
        finally {
            _isBusy = false;
            UpdateButtons();
        }
    }

    private async void ToggleAutoRefresh()
    {
        try {
            var isOn = !UserCustomData.GetBool(AutoRefreshKey);
            await UserCustomData.SetBool(AutoRefreshKey, isOn, CancellationToken.None);
            if (isOn) StartPeriodicRefresh();
            else StopPeriodicRefresh();
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    // every few seconds while there is a session to learn from, without the loading skeleton
    private void StartPeriodicRefresh()
    {
        if (_refreshTimer.IsEnabled || AppModel.State.ConnectionState == AppConnectionState.None || !UserCustomData.GetBool(AutoRefreshKey))
            return;
        _refreshTimer.Start();
    }

    private void StopPeriodicRefresh()
    {
        _refreshTimer.Stop();
    }

    private async void ChooseFilter(string? filter)
    {
        try {
            _filter = filter;
            await RefreshList();
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnAddClick(object? sender, RoutedEventArgs e)
    {
        try {
            if (await _host.ShowDialog(new ProxyEditDialog(_host, ProxySheetKind.Add, null)))
                await RefreshList();
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnAddListClick(object? sender, RoutedEventArgs e)
    {
        try {
            if (await _host.ShowDialog(new ProxyEditDialog(_host, ProxySheetKind.AddList, null)))
                await RefreshList();
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnProxyClick(object? sender, EventArgs e)
    {
        try {
            if (sender is not ProxyListItem item)
                return;
            if (await _host.ShowDialog(new ProxyEditDialog(_host, ProxySheetKind.Edit, item.Proxy)))
                await RefreshList();
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnFirstPageClick(object? sender, RoutedEventArgs e)
    {
        try {
            await GoToPage(1);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }
    private async void OnPrevPageClick(object? sender, RoutedEventArgs e)
    {
        try {
            await GoToPage(_page - 1);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }
    private async void OnNextPageClick(object? sender, RoutedEventArgs e)
    {
        try {
            await GoToPage(_page + 1);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }
    private async void OnLastPageClick(object? sender, RoutedEventArgs e)
    {
        try {
            await GoToPage((_totalCount + ItemsPerPage - 1) / ItemsPerPage);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async Task GoToPage(int page)
    {
        _page = Math.Max(1, page);
        await LoadProxies(showLoading: true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopPeriodicRefresh();
        base.OnDetachedFromVisualTree(e);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopPeriodicRefresh();
    }
}
