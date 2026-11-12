using Android.Runtime;
using Avalonia.Android;
using VpnHood.AppLib.App;
using VpnHood.AppLib.App.Android;
// this file sits in a namespace called Avalonia, so the framework type needs an explicit name
using AvaloniaApplication = Avalonia.Application;

namespace VpnHood.AppUi.Hosting.Avalonia.Android;

/// <summary>
/// Represents the Avalonia UI's application on Android: a head's application derives from it, says how its app starts
/// (<see cref="CreateInitParams"/>), and carries only what the system reads, its <c>[Application]</c> attribute.
/// </summary>
/// <remarks>
/// It starts the app before Avalonia: the platform, then the app's API handed to the UI, then Avalonia. Android makes it
/// in every process of the package, the VPN service's and the Quick Settings tile's too, where neither the app nor
/// Avalonia starts, as they would only lengthen those processes' start. A head that must do something before all this,
/// as a crash reporter that starts in every process, overrides <see cref="OnCreate"/> and calls this one after; its
/// trackers it lists in the app's options.
/// </remarks>
/// <typeparam name="TUi">The UI's application.</typeparam>
/// <param name="javaReference">The handle of the Java object.</param>
/// <param name="transfer">How the handle is owned.</param>
public abstract class AndroidAvaloniaApplication<TUi>(IntPtr javaReference, JniHandleOwnership transfer)
    : AvaloniaAndroidApplication<TUi>(javaReference, transfer)
    where TUi : AvaloniaApplication, IAvaloniaUi, new()
{
    /// <summary>Creates the head's init params, asked for only in the app's own process.</summary>
    /// <returns>The init params.</returns>
    protected abstract AppInitParams CreateInitParams();

    /// <inheritdoc/>
    /// <remarks>
    /// In the app's own process it starts the app and hands the UI the app's API before Avalonia starts; in the package's
    /// other processes it starts neither.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The process's or the package's identity cannot be read; or, in the app's own process, Android names no cache folder,
    /// the init params name no storage folder, the platform has no local app data folder, Android names no folder kept out
    /// of backups, or a debug build's storage check fails.
    /// </exception>
    public override void OnCreate()
    {
        // Avalonia's first class check opens the user's culture's ICU collator on the main thread
        // (AvaloniaUI/Avalonia#22582). Opening it ahead, on a background thread, was tried and
        // dropped: on budget phones the collator takes 0.1-0.15 s of a 9.5 s first start.
        VpnHoodAndroidApp.Init(CreateInitParams);
        if (!VpnHoodApp.IsInit)
            return; // Avalonia's OnCreate starts it; Android's own does nothing

        AvaloniaUiHosting.InitAsync(VpnHoodApp.Instance.Api, CancellationToken.None).GetAwaiter().GetResult();
        base.OnCreate();
    }

    /// <inheritdoc/>
    /// <remarks>It disposes the app first. Android calls it only on an emulator; a device ends the process without it.</remarks>
    public override void OnTerminate()
    {
        if (VpnHoodAndroidApp.IsInit)
            VpnHoodAndroidApp.Instance.Dispose();

        base.OnTerminate();
    }
}
