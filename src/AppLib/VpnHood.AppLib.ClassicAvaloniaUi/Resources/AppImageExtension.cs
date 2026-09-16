using Avalonia.Markup.Xaml;

namespace VpnHood.AppLib.ClassicAvaloniaUi.Resources;

// An image of the assets folder, in XAML: Source="{res:AppImage rocket.webp}". The folder is
// chosen at run time, so the usual avares:// source cannot name it.
public sealed class AppImageExtension(string name) : MarkupExtension
{
    public string Name { get; } = name;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return AppAssets.Image(Name);
    }
}
