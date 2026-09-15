using Avalonia.Controls;
using Avalonia.Media;
using VpnHood.AppLib.AvaloniaUI.Controls;
using VpnHood.AppLib.AvaloniaUI.Resources;

namespace VpnHood.AppLib.AvaloniaUI.Views;

public partial class LanguageView : UserControl, IPage
{
    private const string SystemDefault = "sys";

    private readonly MainView _host;
    private readonly VpnHoodApp _app = VpnHoodApp.Instance;
    private readonly List<(string Code, OptionRow Row)> _rows = [];

    public LanguageView(MainView host)
    {
        _host = host;
        InitializeComponent();

        var s = Strings.Current;
        var state = _app.State;
        var systemCulture = state.SystemUiCultureInfo;
        var cultures = _app.Services.CultureProvider.AvailableCultures
            .Select(x => new UiCultureInfo(x))
            .OrderBy(x => x.NativeName, StringComparer.CurrentCulture)
            .ToArray();

        // the device's language, with the note that it is not one the app has when it is not - a
        // regional culture counts as had when its language is (en-US by en), as the app's own
        // best-culture choice counts it
        var isSystemSupported = IsSupported(systemCulture.Code, cultures.Select(x => x.Code));
        AddRow(SystemDefault, $"{s.SystemDefaultLanguage} ({systemCulture.NativeName})",
            isSystemSupported ? null : s.SystemDefaultLanguageNotSupportedDesc);
        foreach (var culture in cultures)
            AddRow(culture.Code, culture.NativeName, null);

        ShowChoice();
    }

    private static bool IsSupported(string systemCode, IEnumerable<string> availableCodes)
    {
        var available = availableCodes.ToArray();
        for (var culture = new System.Globalization.CultureInfo(systemCode); culture.Name.Length > 0; culture = culture.Parent) {
            if (available.Contains(culture.Name, StringComparer.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private void AddRow(string code, string title, string? description)
    {
        // the note under the system row reads left to right in every language, as language.vue pins it
        var row = new OptionRow { Title = title, Description = description, DescriptionFlowDirection = FlowDirection.LeftToRight };
        row.Clicked += (_, _) => Choose(code);
        _rows.Add((code, row));
        Rows.Children.Add(row);
    }

    private string CurrentCode => _app.UserSettings.CultureCode ?? SystemDefault;

    private void ShowChoice()
    {
        foreach (var (code, row) in _rows)
            row.IsChecked = string.Equals(code, CurrentCode, StringComparison.OrdinalIgnoreCase);
    }

    // the choice is saved and the app re-reads its culture; the words follow on the next beat
    private void Choose(string code)
    {
        _app.UserSettings.CultureCode = code == SystemDefault ? null : code;
        _app.SettingsService.Save();
        ShowChoice();
        _host.ViewModel.Refresh();
        Header.Title = Strings.Current.Language;
    }

    public void FocusDefault()
    {
        var chosen = _rows.FirstOrDefault(x => x.Row.IsChecked).Row ?? _rows[0].Row;
        chosen.LandFocus();
    }
}
