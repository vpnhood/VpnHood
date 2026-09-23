using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using VpnHood.AppUi.Common;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Resources;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Styles;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Views;
using VpnHood.Core.Toolkit.Assets;
using VpnHood.Core.Toolkit.Extensions;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia;

// VpnHood's own Avalonia UI: the web UI's pages, page for page, in one responsive layout. A head
// names this class as its UI (AvaloniaDesktopHost.Run<ClassicAvaloniaApp>, the Android
// application's type argument, the iOS delegate's) and gets these pages, these controls and these
// two palettes; a head that names another gets that one instead, and none of this is in its build.
public class ClassicAvaloniaApp : VpnHoodAvaloniaAppBase, IAvaloniaUi
{
    // The words this UI has, which the head declares to the app (VhApp.Configure).
    public static IReadOnlyList<string> AvailableCultures => Strings.AvailableCultures;

    // What this UI needs before its first view, out of the store the head hands in: the words, and
    // the fonts. The head calls it, so the moment is the head's - on Android before the activity's
    // view, from a browser page once the API is up - and the fonts are registered as soon as
    // Avalonia can take them (AppAssets.PrepareAsync).
    public static async Task PrepareContentAsync(IAssetProvider assets, CancellationToken cancellationToken)
    {
        await Strings.InitAsync(assets, cancellationToken).Vhc();
        await AppAssets.PrepareAsync(assets, cancellationToken).Vhc();
    }

    public override void Initialize()
    {
        // The fonts of the store, before the styles that name them are read with the XAML below -
        // where the head has prepared the content by now (a desktop host, iOS, a browser).
        // Android's Application starts Avalonia before its activity prepares the content - and in
        // processes that never get one - so there the preparing registers them itself, before the
        // activity's view.
        if (AppAssets.IsPrepared)
            AppAssets.RegisterFonts();

        // The palettes first, both of them, and only then the XAML: the styles it loads name their
        // keys with StaticResource, which is read as they load and never again - so a palette that
        // arrives afterwards paints nothing. Blue is the default; the look's own palette is merged
        // over it, where the head hands the UI the app's API first (the processes that get no view
        // keep the default), and a dictionary merged later wins.
        Resources.MergedDictionaries.Add(new BlueTheme());
        var themeOverride = AppTheme.OverrideFor(VhApp.IsInit ? VhApp.Features.UiTheme : null);
        if (themeOverride != null)
            Resources.MergedDictionaries.Add(themeOverride);

        AvaloniaXamlLoader.Load(this);
    }

    protected override Control CreateMainView()
    {
        return new MainView();
    }
}
