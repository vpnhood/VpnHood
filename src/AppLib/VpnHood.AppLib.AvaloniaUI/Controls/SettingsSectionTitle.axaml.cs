using Avalonia.Controls;

namespace VpnHood.AppLib.AvaloniaUI.Controls;

public partial class SettingsSectionTitle : UserControl
{
    public SettingsSectionTitle()
    {
        InitializeComponent();
    }

    public string Title {
        get => TitleText.Text ?? "";
        set => TitleText.Text = value;
    }
}
