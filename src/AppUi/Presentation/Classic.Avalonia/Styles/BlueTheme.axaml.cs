using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Styles;

// The blue palette as a type, so the App can merge it before it loads its own XAML - which is what
// the styles need (StaticResource is read once). The keys are BlueTheme.axaml's; nothing here but
// the load.
public partial class BlueTheme : ResourceDictionary
{
    public BlueTheme()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
