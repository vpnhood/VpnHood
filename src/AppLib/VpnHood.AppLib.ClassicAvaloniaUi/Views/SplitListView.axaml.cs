using Avalonia.Controls;
using VpnHood.AppLib.AvaloniaUI;
using VpnHood.AppLib.ClassicAvaloniaUi.Controls;
using VpnHood.AppLib.ClassicAvaloniaUi.Helpers;
using VpnHood.AppLib.Contracts.Settings;

namespace VpnHood.AppLib.ClassicAvaloniaUi.Views;

// The page behind SplitDomainsView, SplitIpsViaAppView and SplitIpsViaDeviceView: each names its
// title, its switch and the lists it edits; this holds the loading, the leave and the revert.
public abstract partial class SplitListView : UserControl, IPage, ILeaveGuard
{
    private readonly MainView _host;
    private (string Excludes, string Includes, string Blocks) _saved;
    private bool _isLoaded;

    protected SplitListView(MainView host)
    {
        _host = host;
        InitializeComponent();
    }

    protected MainView Host => _host;

    // the settings the switch is in, saved as one (AppModel.SaveUserSettings) once the switch moved
    protected static UserSettings Settings => AppModel.UserSettings;

    protected abstract string Title { get; }
    protected abstract string? SwitchDescription { get; }
    protected abstract bool IsSwitchOn { get; set; }
    protected abstract Task<(string Excludes, string Includes, string Blocks)> Load(CancellationToken cancellationToken);
    protected abstract Task Save(string excludes, string includes, string blocks, CancellationToken cancellationToken);
    protected abstract void ConfigureInput(SplitListInput input);

    // an alert under the switch: the server that undoes a domain filter
    protected virtual string? SwitchWarning => null;

    // The pages' own construction finishes before the lists can be read. The lists come through
    // the API, so the page shows its switch at once and its lists as they arrive.
    protected void Initialize()
    {
        Header.Title = Title;
        EnabledItem.Title = Title;
        EnabledItem.Description = SwitchDescription;
        EnabledItem.IsOn = IsSwitchOn;
        if (SwitchWarning is { } warning) {
            var alert = new Border { Child = new TextBlock { Text = warning, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, Margin = new Avalonia.Thickness(0, 8, 0, 0) };
            alert.Classes.Add("alert-warning");
            EnabledItem.Extra = alert;
        }
        ConfigureInput(Input);
        ShowGate();
        _ = LoadLists();
    }

    private async Task LoadLists()
    {
        try {
            _saved = await Load(CancellationToken.None);
            Input.Excludes = _saved.Excludes;
            Input.Includes = _saved.Includes;
            Input.Blocks = _saved.Blocks;
            _isLoaded = true;
        }
        catch (Exception ex) {
            await _host.ProcessError(ex);
        }
    }

    private void ShowGate()
    {
        var isSplitOn = Settings.SplitTunneling.Enabled;
        EnabledItem.IsDisabled = !isSplitOn;
        Input.IsDisabled = !isSplitOn || !IsSwitchOn;
    }

    private bool IsDirty => _isLoaded && (Input.Excludes != _saved.Excludes || Input.Includes != _saved.Includes || Input.Blocks != _saved.Blocks);

    private async void OnToggled(object? sender, EventArgs e)
    {
        try {
            var settings = Settings;
            IsSwitchOn = EnabledItem.IsOn;
            await AppModel.SaveUserSettings(settings, CancellationToken.None);
            ShowGate();
            _host.ViewModel.Refresh();
        }
        catch (Exception ex) {
            await _host.ProcessError(ex);
        }
    }

    private void OnTurnedOn(object? sender, EventArgs e)
    {
        ShowGate();
    }

    private void OnInputChanged(object? sender, EventArgs e)
    {
        if (_isLoaded)
            RevertButton.IsVisible = RevertButton.IsVisible && IsDirty;
    }

    private void OnRevertClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Input.Excludes = _saved.Excludes;
        Input.Includes = _saved.Includes;
        Input.Blocks = _saved.Blocks;
        RevertButton.IsVisible = false;
    }

    // Saved on the way out, and only when something changed. A list the app refuses keeps the page
    // open with the reason and offers to revert it.
    public async Task<bool> CanLeave()
    {
        if (!IsDirty)
            return true;

        try {
            await Save(Input.Excludes, Input.Includes, Input.Blocks, CancellationToken.None);
            await AppModel.SaveUserSettings(Settings, CancellationToken.None);
            _host.ViewModel.Refresh();
            return true;
        }
        catch (Exception ex) {
            RevertButton.IsVisible = true;
            await _host.ProcessError(ex);
            return false;
        }
    }

    public void FocusDefault()
    {
        if (!EnabledItem.IsDisabled) EnabledItem.FindFirstButton()?.LandFocus();
        else Header.FocusBack();
    }
}
