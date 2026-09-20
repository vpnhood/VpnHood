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

        AvaloniaXamlLoader.Load(this);

        // The look's palette over the blue the XAML merged, before any style is applied: the
        // head hands the UI the app's API first, except in the processes that get no view, which
        // keep the default.
        var themeOverride = AppTheme.OverrideFor(VhApp.IsInit ? VhApp.Features.UiTheme : null);
        if (themeOverride != null)
            Resources.MergedDictionaries.Add(themeOverride);
    }

    protected override Control CreateMainView()
    {
        return new MainView();
    }
}
