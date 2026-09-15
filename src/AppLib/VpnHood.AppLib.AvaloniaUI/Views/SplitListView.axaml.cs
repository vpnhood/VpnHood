using Avalonia.Controls;
using VpnHood.AppLib.AvaloniaUI.Controls;
using VpnHood.AppLib.AvaloniaUI.Helpers;

namespace VpnHood.AppLib.AvaloniaUI.Views;

// The page behind SplitDomainsView, SplitIpsViaAppView and SplitIpsViaDeviceView: each names its
// title, its switch and the lists it edits; this holds the loading, the leave and the revert.
public abstract partial class SplitListView : UserControl, IPage, ILeaveGuard
{
    private readonly MainView _host;
    private readonly VpnHoodApp _app = VpnHoodApp.Instance;
    private (string Excludes, string Includes, string Blocks) _saved;
    private bool _isLoaded;

    protected SplitListView(MainView host)
    {
        _host = host;
        InitializeComponent();
    }

    protected MainView Host => _host;
    protected VpnHoodApp App => _app;

    protected abstract string Title { get; }
    protected abstract string? SwitchDescription { get; }
    protected abstract bool IsSwitchOn { get; set; }
    protected abstract (string Excludes, string Includes, string Blocks) Load();
    protected abstract void Save(string excludes, string includes, string blocks);
    protected abstract void ConfigureInput(SplitListInput input);

    // an alert under the switch: the server that undoes a domain filter
    protected virtual string? SwitchWarning => null;

    // the pages' own construction finishes before the lists can be read
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

        _saved = Load();
        Input.Excludes = _saved.Excludes;
        Input.Includes = _saved.Includes;
        Input.Blocks = _saved.Blocks;
        _isLoaded = true;
        ShowGate();
    }

    private void ShowGate()
    {
        var isSplitOn = _app.UserSettings.SplitTunneling.Enabled;
        EnabledItem.IsDisabled = !isSplitOn;
        Input.IsDisabled = !isSplitOn || !IsSwitchOn;
    }

    private bool IsDirty => Input.Excludes != _saved.Excludes || Input.Includes != _saved.Includes || Input.Blocks != _saved.Blocks;

    private void OnToggled(object? sender, EventArgs e)
    {
        IsSwitchOn = EnabledItem.IsOn;
        _app.SettingsService.Save();
        ShowGate();
        _host.ViewModel.Refresh();
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
            Save(Input.Excludes, Input.Includes, Input.Blocks);
            _app.SettingsService.Save();
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
