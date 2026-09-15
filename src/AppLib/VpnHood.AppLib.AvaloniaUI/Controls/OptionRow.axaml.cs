using Avalonia.Controls;
using Avalonia.Interactivity;
using VpnHood.AppLib.AvaloniaUI.Helpers;
using VpnHood.AppLib.AvaloniaUI.Resources;

namespace VpnHood.AppLib.AvaloniaUI.Controls;

public partial class OptionRow : UserControl
{
    public event EventHandler? Clicked;

    public OptionRow()
    {
        InitializeComponent();
        UpdateMark();
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

    // a chip beside the title: "Recommended", "Default"
    public string? ChipLabel {
        get => ChipText.Text;
        set {
            ChipText.Text = value;
            Chip.IsVisible = !string.IsNullOrEmpty(value);
        }
    }

    // a second chip, in a colour of its own, is rare enough to be a second call
    public void SetChip(string text, string chipClass)
    {
        ChipText.Text = text;
        Chip.IsVisible = true;
        Chip.Classes.Set("highlight", chipClass == "highlight");
        Chip.Classes.Set("on-note", chipClass == "on-note");
    }

    // a checkbox rather than a radio
    public bool IsCheckBox {
        get;
        set {
            field = value;
            UpdateMark();
        }
    }

    public bool IsChecked {
        get;
        set {
            field = value;
            UpdateMark();
        }
    }

    public bool IsDisabled {
        get => !Row.IsEnabled;
        set => Row.IsEnabled = !value;
    }

    // the group's rows come here to leave a control under the remote
    public void LandFocus()
    {
        Row.LandFocus();
    }

    private void UpdateMark()
    {
        Mark.Text = IsCheckBox
            ? IsChecked ? Mdi.CheckboxMarked : Mdi.CheckboxBlankOutline
            : IsChecked ? Mdi.RadioboxMarked : Mdi.RadioboxBlank;
        Row.Classes.Set("checked", IsChecked);
    }

    private void OnRowClick(object? sender, RoutedEventArgs e)
    {
        Clicked?.Invoke(this, EventArgs.Empty);
    }
}
