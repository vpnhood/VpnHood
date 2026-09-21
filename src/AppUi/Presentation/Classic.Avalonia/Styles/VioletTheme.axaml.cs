using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Styles;

// The violet palette as a type, which is all this file is for: a dictionary asked for by Uri alone
// is not a reference the trimmer can follow, and the browser bundle is trimmed to the full extent.
// The keys are VioletTheme.axaml's; nothing here but the load.
public class VioletTheme : ResourceDictionary
{
    public VioletTheme()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
