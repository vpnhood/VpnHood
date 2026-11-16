using Avalonia.Controls;

namespace VpnHood.AppUi.Avalonia.Classic.Controls;

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
