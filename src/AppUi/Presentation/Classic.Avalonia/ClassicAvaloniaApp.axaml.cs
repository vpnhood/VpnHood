using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using VpnHood.AppLib.Assets;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Resources;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Styles;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Views;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia;

// VpnHood's own Avalonia UI: the web UI's pages, page for page, in one responsive layout. A head
// names this class as its UI (AvaloniaDesktopHost.Run<ClassicAvaloniaApp>, the Android
// application's type argument, the iOS delegate's) and gets these pages, these controls and these
// two palettes; a head that names another gets that one instead, and none of this is in its build.
public class ClassicAvaloniaApp : VpnHoodAvaloniaAppBase, IAvaloniaUi
{
    // The words this UI has, which the head declares to the app (AppModel.Configure).
    public static IReadOnlyList<string> AvailableCultures => Strings.AvailableCultures;

    // The pictures: the folder of the content package, which on Android is a copy this call makes
    // (AndroidAppContent) - here, where a head chooses the moment, rather than under the first
    // page that asks for a picture. The fonts follow it, where Avalonia is already up; where it is
    // not, Initialize registers them as the styles that name them are read.
    public static void PrepareContent()
    {
        _ = AppContent.FolderPath;
        if (Application.Current != null)
            AppAssets.RegisterFonts();
    }

    public override void Initialize()
    {
        // The fonts of the assets folder, before the styles that name them are read with the XAML
        // below - where the head has made the folder ready by now (PrepareContent). A desktop
        // host, iOS and a browser have; Android's Application starts Avalonia before its activity
        // prepares the content - and in processes that never get one - so there the activity
        // registers them, before its view. Asked, never resolved: the resolving is a copy on
        // Android, and its moment is the head's.
        if (AppContent.IsResolved)
            AppAssets.RegisterFonts();

        AvaloniaXamlLoader.Load(this);

        // The product's palette over the client's the XAML merged, before any style is applied: the
        // head hands the UI the app's API first, except in the processes that get no view, which
        // keep the default.
        var themeOverride = AppTheme.OverrideFor(AppModel.IsInit ? AppModel.Features.UiName : null);
        if (themeOverride != null)
            Resources.MergedDictionaries.Add(themeOverride);
    }

    protected override Control CreateMainView()
    {
        return new MainView();
    }
}
