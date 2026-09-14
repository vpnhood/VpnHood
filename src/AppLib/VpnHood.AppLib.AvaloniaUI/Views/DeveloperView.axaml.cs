using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using VpnHood.AppLib.AvaloniaUI.Helpers;
using VpnHood.AppLib.AvaloniaUI.ViewModels;

namespace VpnHood.AppLib.AvaloniaUI.Views;

// The debug fields of UserSettings, edited as the web UI's developer dialog edits them: DebugData1
// a space-separated set of commands, DebugData2 free text, and both saved when the page closes -
// through Close or through Back, which is the same thing here. A command the app does not know is
// not shown but is not lost either: it is kept and written back beside the ones that are.
public partial class DeveloperView : UserControl, IPage, IDisposable
{
    private readonly MainView _host;
    private readonly VpnHoodApp _app = VpnHoodApp.Instance;
    private readonly DebugCommandItem[] _commands;
    private readonly string[] _unknown;

    public DeveloperView(MainView host)
    {
        _host = host;
        InitializeComponent();

        var current = (_app.UserSettings.DebugData1 ?? "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _commands = _app.Features.DebugCommands
            .Select(x => new DebugCommandItem(x) { IsOn = current.Contains(x) })
            .ToArray();
        _unknown = current.Except(_app.Features.DebugCommands).ToArray();

        SupportIdText.Text = $"Support ID: {_app.CurrentClientProfileInfo?.SupportId}";
        CommandList.ItemsSource = _commands;
        DebugData2Box.Text = _app.UserSettings.DebugData2;

        foreach (var command in _commands)
            command.PropertyChanged += (_, _) => ShowChosen();
        ShowChosen();
    }

    // The commands are what this page is opened for, and the field is one key from both the
    // fields under it and the back button above.
    public void FocusDefault()
    {
        CommandsField.LandFocus();
    }

    // The field shows what the list has on, as the combobox shows its chips.
    private void ShowChosen()
    {
        var chosen = _commands.Where(x => x.IsOn).ToArray();
        ChosenList.ItemsSource = chosen;
        NoCommandText.IsVisible = chosen.Length == 0;
    }

    private void OnLogClick(object? sender, RoutedEventArgs e)
    {
        // the dialog's "Open log", which opens the app web server's log.txt in a browser tab; this
        // head has no browser, so the log is a page of its own
        Save();
        _host.Navigate(new LogView());
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        _host.GoBack();
    }

    // Every way out saves: Close and Back pop the page (Dispose), and a host that tears the view
    // down without popping it still detaches this one. Saving twice costs nothing - it is a no-op
    // when nothing differs - and the page is shown again when the log page above it is closed.
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Save();
        base.OnDetachedFromVisualTree(e);
    }

    public void Dispose()
    {
        Save();
    }

    // Saving applies the settings, so it happens only when something differs: a debug command takes
    // effect at the next launch, but the save itself reaches the running app.
    private void Save()
    {
        var commands = _commands.Where(x => x.IsOn).Select(x => x.Command).Concat(_unknown);
        var debugData1 = string.Join(' ', commands);
        var debugData2 = DebugData2Box.Text?.Trim();

        var newData1 = debugData1.Length > 0 ? debugData1 : null;
        var newData2 = debugData2?.Length > 0 ? debugData2 : null;
        if (newData1 == _app.UserSettings.DebugData1 && newData2 == _app.UserSettings.DebugData2)
            return;

        _app.UserSettings.DebugData1 = newData1;
        _app.UserSettings.DebugData2 = newData2;
        _app.SettingsService.Save();
    }
}
