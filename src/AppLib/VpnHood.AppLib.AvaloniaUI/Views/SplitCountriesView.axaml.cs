using Avalonia.Controls;
using VpnHood.AppLib.AvaloniaUI.Resources;
using VpnHood.AppLib.AvaloniaUI.ViewModels;
using VpnHood.AppLib.Settings;

namespace VpnHood.AppLib.AvaloniaUI.Views;

public partial class SplitCountriesView : UserControl, IPage, ILeaveGuard
{
    private readonly MainView _host;
    private readonly VpnHoodApp _app = VpnHoodApp.Instance;
    private IReadOnlyList<FilterItem> _items = [];
    private bool _isListLoaded;

    public SplitCountriesView(MainView host)
    {
        _host = host;
        InitializeComponent();
        var s = Strings.Current;
        IncludeAllRow.Title = s.IncludeAll;
        IncludeAllRow.Description = s.IncludeAllDesc;
        ExcludeMyCountryRow.Title = s.ExcludeMyCountry;
        ExcludeMyCountryRow.Description = s.SplitExcludeMyCountryDesc;
        ExcludeMyCountryRow.ChipLabel = s.Recommended;
        ExcludeListRow.Title = s.CustomIncludeList;
        ExcludeListRow.Description = s.CustomIncludeListDesc;
        Show();
    }

    private SplitTunnelingSettings Split => _app.UserSettings.SplitTunneling;
    private bool IsListMode => Split.CountryMode == SplitCountryMode.ExcludeList;

    private void Show()
    {
        var isEnabled = Split.Enabled;
        IncludeAllRow.IsChecked = Split.CountryMode == SplitCountryMode.IncludeAll;
        ExcludeMyCountryRow.IsChecked = Split.CountryMode == SplitCountryMode.ExcludeMyCountry;
        ExcludeListRow.IsChecked = IsListMode;
        IncludeAllRow.IsDisabled = !isEnabled;
        ExcludeMyCountryRow.IsDisabled = !isEnabled;
        ExcludeListRow.IsDisabled = !isEnabled;
        ModeCard.Opacity = isEnabled ? 1 : 0.5;
        List.IsVisible = IsListMode;
        List.IsDisabled = !isEnabled;
        if (IsListMode && !_isListLoaded)
            _ = LoadCountries();
    }

    // The countries the split database knows, the excluded ones OFF. The smaller group comes first,
    // so the exceptions are in view whichever way the list leans (split-countries.vue).
    private async Task LoadCountries()
    {
        _isListLoaded = true;
        List.IsLoading = true;
        try {
            var excluded = Split.Countries;
            var countries = await _app.Services.SplitCountryService.GetSupportedSplitCountries(CancellationToken.None);
            var mapped = countries.Select(x => new FilterItem {
                Id = x.CountryCode,
                Name = x.TranslatedName,
                Icon = AppAssets.Flag(x.CountryCode),
                IsSelected = !excluded.Contains(x.CountryCode, StringComparer.OrdinalIgnoreCase)
            }).ToArray();

            var onCount = mapped.Count(x => x.IsSelected);
            var isOnMinority = onCount < mapped.Length - onCount;
            _items = mapped.OrderBy(x => x.IsSelected == isOnMinority ? 0 : 1)
                .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            List.Items = _items;
        }
        catch (Exception ex) {
            _isListLoaded = false;
            await _host.ProcessError(ex);
        }
        finally {
            List.IsLoading = false;
        }
    }

    private void Choose(SplitCountryMode mode)
    {
        if (mode == SplitCountryMode.ExcludeMyCountry)
            Split.Countries = [];
        Split.CountryMode = mode;
        _app.SettingsService.Save();
        Show();
        _host.ViewModel.Refresh();
    }

    private void OnIncludeAllClick(object? sender, EventArgs e) => Choose(SplitCountryMode.IncludeAll);
    private void OnExcludeMyCountryClick(object? sender, EventArgs e) => Choose(SplitCountryMode.ExcludeMyCountry);
    private void OnExcludeListClick(object? sender, EventArgs e) => Choose(SplitCountryMode.ExcludeList);

    // the setting follows the switches; the save waits for the leave, as it does in the web UI
    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        Split.Countries = _items.Where(x => !x.IsSelected).Select(x => x.Id).ToArray();
    }

    private void OnTurnedOn(object? sender, EventArgs e)
    {
        Show();
    }

    public async Task<bool> CanLeave()
    {
        if (IsListMode && Split.Countries.Length >= AppData.AllCountriesCount) {
            await _host.ShowError(Strings.Current.AllCountriesExcludedErrorMsg);
            return false;
        }

        _app.SettingsService.Save();
        _host.ViewModel.Refresh();
        return true;
    }

    public void FocusDefault()
    {
        if (!Split.Enabled) {
            Header.FocusBack();
            return;
        }

        var chosen = IncludeAllRow.IsChecked ? IncludeAllRow : ExcludeMyCountryRow.IsChecked ? ExcludeMyCountryRow : ExcludeListRow;
        chosen.LandFocus();
    }
}
