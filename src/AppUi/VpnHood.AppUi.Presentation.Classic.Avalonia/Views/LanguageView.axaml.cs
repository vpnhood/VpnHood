using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using VpnHood.AppUi.Common;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Controls;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views;

public partial class LanguageView : UserControl, IPage
{
    private const string SystemDefault = "sys";

    private readonly MainView _host;
    private readonly List<(string Code, OptionRow Row)> _rows = [];
    private readonly OptionRow _systemRow;
    private readonly string _systemCultureName;
    private readonly bool _isSystemSupported;

    public LanguageView(MainView host)
    {
        _host = host;
        InitializeComponent();

        var systemCulture = VhApp.State.SystemUiCultureInfo;
        var cultures = VhApp.AvailableCultureInfos
            .OrderBy(x => x.NativeName, StringComparer.CurrentCulture)
            .ToArray();

        // the device's language, with the note that it is not one the app has when it is not - a
        // regional culture counts as had when its language is (en-US by en), as the app's own
        // best-culture choice counts it
        _systemCultureName = systemCulture.NativeName;
        _isSystemSupported = IsSupported(systemCulture.Code, cultures.Select(x => x.Code));
        _systemRow = AddRow(SystemDefault);
        foreach (var culture in cultures)
            AddRow(culture.Code).Title = culture.NativeName;

        ShowChoice();
    }

    // The system row is in the app's language, unlike the others, so it is written on arrival and again
    // when the words change: MainViewModel then raises every property, on the UI thread. The title
    // follows by its binding.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _host.ViewModel.PropertyChanged += OnViewModelChanged;
        ShowSystemRow();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _host.ViewModel.PropertyChanged -= OnViewModelChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "")
            ShowSystemRow();
    }

    private void ShowSystemRow()
    {
        var s = Strings.Current;
        _systemRow.Title = $"{s.SystemDefaultLanguage} ({_systemCultureName})";
        _systemRow.Description = _isSystemSupported ? null : s.SystemDefaultLanguageNotSupportedDesc;
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

    private OptionRow AddRow(string code)
    {
        // the note under the system row reads left to right in every language, as language.vue pins it
        var row = new OptionRow { DescriptionFlowDirection = FlowDirection.LeftToRight };
        row.Clicked += (_, _) => Choose(code);
        _rows.Add((code, row));
        Rows.Children.Add(row);
        return row;
    }

    private static string CurrentCode => VhApp.UserSettings.CultureCode ?? SystemDefault;

    private void ShowChoice()
    {
        foreach (var (code, row) in _rows)
            row.IsChecked = string.Equals(code, CurrentCode, StringComparison.OrdinalIgnoreCase);
    }

    // the choice is saved and the app re-reads its culture; the words follow on the next beat
    private async void Choose(string code)
    {
        try {
            var settings = VhApp.UserSettings;
            settings.CultureCode = code == SystemDefault ? null : code;
            await VhApp.SaveUserSettings(settings, CancellationToken.None);
            ShowChoice();
            _host.ViewModel.Refresh();
        }
        catch (Exception ex) {
            await _host.ProcessError(ex);
        }
    }

    public void FocusDefault()
    {
        var chosen = _rows.FirstOrDefault(x => x.Row.IsChecked).Row ?? _rows[0].Row;
        chosen.LandFocus();
    }
}
