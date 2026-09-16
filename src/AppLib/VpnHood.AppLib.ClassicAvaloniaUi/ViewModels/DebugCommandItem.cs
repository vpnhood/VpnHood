using System.ComponentModel;

namespace VpnHood.AppLib.ClassicAvaloniaUi.ViewModels;

// One row of the developer page: a debug command the app knows (AppFeatures.DebugCommands), and
// whether DebugData1 names it.
public sealed class DebugCommandItem(string command) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Command { get; } = command;

    public bool IsOn {
        get;
        set {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOn)));
        }
    }
}
