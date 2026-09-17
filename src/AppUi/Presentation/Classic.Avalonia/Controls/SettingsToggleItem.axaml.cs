using Avalonia.Controls;
using Avalonia.Interactivity;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Controls;

public partial class SettingsToggleItem : UserControl
{
    // raised after the press flipped the switch; the caller writes the setting
    public event EventHandler? Toggled;

    public SettingsToggleItem()
    {
        InitializeComponent();
    }

    public string Title {
        get => TitleText.Text ?? "";
        set => TitleText.Text = value;
    }

    public string? Description {
        get => DescriptionText.Text;
        set {
            DescriptionText.Text = value;
            DescriptionText.IsVisible = !string.IsNullOrEmpty(value);
        }
    }

    public string? Warning {
        get => WarningText.Text;
        set {
            WarningText.Text = value;
            WarningChip.IsVisible = !string.IsNullOrEmpty(value);
        }
    }

    public bool IsOn {
        get => Switch.IsChecked == true;
        set => Switch.IsChecked = value;
    }

    // disables the switch only; the stored value keeps showing
    public bool IsDisabled {
        get => !Card.IsEnabled;
        set => Card.IsEnabled = !value;
    }

    // extra content under the row, e.g. a conditional alert
    public object? Extra {
        get => ExtraContent.Content;
        set {
            ExtraContent.Content = value;
            ExtraContent.IsVisible = value != null;
        }
    }

    private void OnCardClick(object? sender, RoutedEventArgs e)
    {
        IsOn = !IsOn;
        Toggled?.Invoke(this, EventArgs.Empty);
    }
}
