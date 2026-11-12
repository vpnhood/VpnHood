using Android.Runtime;
using VpnHood.AppLib.App;
using VpnHood.AppLib.App.Android;

namespace VpnHood.AppUi.Hosting.WebView.Android;

/// <summary>
/// Represents the web view UI's application on Android: a head's application derives from it, says how its app starts
/// (<see cref="CreateInitParams"/>), and carries only what the system reads, its <c>[Application]</c> attribute.
/// </summary>
/// <remarks>
/// Android makes it in every process of the package, the VPN service's and the Quick Settings tile's too, and the app
/// starts only in the app's own; the page is the activity's. A head that must do something before the app starts, as an
/// analytics SDK that reports from every process, overrides <see cref="OnCreate"/> and calls this one after.
/// </remarks>
/// <param name="javaReference">The handle of the Java object.</param>
/// <param name="transfer">How the handle is owned.</param>
public abstract class AndroidWebViewApplication(IntPtr javaReference, JniHandleOwnership transfer)
    : Application(javaReference, transfer)
{
    /// <summary>Creates the head's init params, asked for only in the app's own process.</summary>
    /// <returns>The init params.</returns>
    protected abstract AppInitParams CreateInitParams();

    /// <inheritdoc/>
    /// <remarks>In the app's own process it starts the app. An ad provider that names countries both to include and to exclude fails the start.</remarks>
    /// <exception cref="OverflowException">A duration in a built-in access key is out of range.</exception>
    /// <exception cref="System.IO.InvalidDataException">A built-in access key decodes to JSON <c>null</c>.</exception>
    /// <exception cref="System.Text.Json.JsonException">A built-in access key holds no valid access key.</exception>
    /// <exception cref="NotSupportedException">A built-in access key's version is not one the app reads.</exception>
    /// <exception cref="FormatException">
    /// A built-in access key is no valid Base64, or an endpoint or a duration in it is not valid.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The process's or the package's identity cannot be read; or, in the app's own process, Android names no cache folder,
    /// the init params name no storage folder, the platform has no local app data folder, Android names no folder kept out
    /// of backups, or a debug build's storage check fails.
    /// </exception>
    public override void OnCreate()
    {
        VpnHoodAndroidApp.Init(CreateInitParams);
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
