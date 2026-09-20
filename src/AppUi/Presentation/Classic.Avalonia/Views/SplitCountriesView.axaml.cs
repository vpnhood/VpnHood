using Avalonia.Controls;
using VpnHood.AppUi.Common;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Resources;
using VpnHood.AppUi.Presentation.Classic.Avalonia.ViewModels;
using VpnHood.AppLib.Api.Settings;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views;

public partial class SplitCountriesView : UserControl, IPage, ILeaveGuard
{
    private readonly MainView _host;
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

    private static SplitTunnelingSettings Split => VhApp.UserSettings.SplitTunneling;
    private static bool IsListMode => Split.CountryMode == SplitCountryMode.ExcludeList;

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
            var countries = await VhApp.Api.App.GetSupportedSplitCountries(CancellationToken.None);
            // the flags together: at once from a folder, in one round from a web server
            var mapped = await Task.WhenAll(countries.Select(async x => new FilterItem {
                Id = x.CountryCode,
                Name = x.TranslatedName,
                Icon = AppAssets.FlagPath(x.CountryCode) is { } flagPath ? await AppAssets.LoadBitmapAsync(flagPath) : null,
                IsSelected = !excluded.Contains(x.CountryCode, StringComparer.OrdinalIgnoreCase)
            }));

            var onCount = mapped.Count(x => x.IsSelected);
            var isOnMinority = onCount < mapped.Length - onCount;
            _items = [.. mapped.OrderBy(x => x.IsSelected == isOnMinority ? 0 : 1)
                .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)];
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

    private async void Choose(SplitCountryMode mode)
    {
        try {
            if (mode == SplitCountryMode.ExcludeMyCountry)
                Split.Countries = [];
            Split.CountryMode = mode;
            await VhApp.SaveUserSettings(VhApp.UserSettings, CancellationToken.None);
            Show();
            _host.ViewModel.Refresh();
        }
        catch (Exception ex) {
            await _host.ProcessError(ex);
        }
    }

    private void OnIncludeAllClick(object? sender, EventArgs e) => Choose(SplitCountryMode.IncludeAll);
    private void OnExcludeMyCountryClick(object? sender, EventArgs e) => Choose(SplitCountryMode.ExcludeMyCountry);
    private void OnExcludeListClick(object? sender, EventArgs e) => Choose(SplitCountryMode.ExcludeList);

    // the setting follows the switches; the save waits for the leave, as it does in the web UI
    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        Split.Countries = [.. _items.Where(x => !x.IsSelected).Select(x => x.Id)];
    }

    private void OnTurnedOn(object? sender, EventArgs e)
    {
        Show();
    }

    public async Task<bool> CanLeave()
    {
        if (IsListMode && Split.Countries.Count >= AppText.AllCountriesCount) {
            await _host.ShowError(Strings.Current.AllCountriesExcludedErrorMsg);
            return false;
        }

        try {
            await VhApp.SaveUserSettings(VhApp.UserSettings, CancellationToken.None);
            _host.ViewModel.Refresh();
            return true;
        }
        catch (Exception ex) {
            await _host.ProcessError(ex);
            return false;
        }
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
