using VpnHood.AppLib.Abstractions.Device;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;

namespace VpnHood.AppLib.Abstractions.Device;

/// <summary>
/// Provides a device UI provider that supports none of the features, as the base of a platform's own: each flag is
/// <see langword="false"/> and each request throws <see cref="NotSupportedException"/>, but a web browser is taken as
/// present.
/// </summary>
public class NullDeviceUiProvider : IDeviceUiProvider
{
    /// <inheritdoc/>
    /// <remarks>It gives <see langword="null"/>.</remarks>
    public virtual PrivateDns? GetPrivateDns() => null;

    /// <inheritdoc/>
    /// <remarks>It gives no height.</remarks>
    public virtual SystemBarsInfo GetBarsInfo(IUiContext uiContext) => SystemBarsInfo.Default;

    /// <inheritdoc/>
    /// <remarks>It does nothing.</remarks>
    public virtual Task KeepScreenOn(IUiContext uiContext, bool value) => Task.CompletedTask;

    /// <inheritdoc/>
    /// <remarks>
    /// It is <see langword="true"/>: every desktop and phone platform has a browser, and a platform that does not is
    /// the one that must say so.
    /// </remarks>
    public virtual bool IsWebBrowserSupported => true;

    /// <inheritdoc/>
    public virtual bool IsQuickLaunchSupported => false;

    /// <inheritdoc/>
    public virtual bool IsRequestQuickLaunchSupported => false;

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always.</exception>
    public virtual Task<bool> RequestQuickLaunch(IUiContext uiContext, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    /// <remarks>It is <see langword="false"/>.</remarks>
    public virtual bool? IsNotificationEnabled => false;

    /// <inheritdoc/>
    public virtual bool IsRequestNotificationSupported => false;

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always.</exception>
    public virtual Task<bool> RequestNotification(IUiContext uiContext, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public virtual bool IsPrivateDnsSettingsSupported => false;

    /// <inheritdoc/>
    public virtual bool IsSettingsSupported => false;

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always.</exception>
    public virtual void OpenSettings(IUiContext uiContext) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public virtual bool IsAlwaysOnSettingsSupported => false;

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always.</exception>
    public virtual void OpenAlwaysOnSettings(IUiContext uiContext) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public virtual bool IsKillSwitchSettingsSupported => false;

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always.</exception>
    public virtual void OpenKillSwitchSettings(IUiContext requiredContext) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public virtual bool IsAppSettingsSupported => false;

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always.</exception>
    public virtual void OpenAppSettings(IUiContext context) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public virtual bool IsAppNotificationSettingsSupported => false;

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always.</exception>
    public virtual void OpenAppNotificationSettings(IUiContext uiContext) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public virtual bool IsShareSupported => false;

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always.</exception>
    public virtual void ShareFile(IUiContext uiContext, string fileName, byte[] content) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public virtual bool IsProxySettingsSupported => false;

    /// <inheritdoc/>
    /// <remarks>It gives <see langword="null"/>.</remarks>
    public virtual DeviceProxySettings? GetProxySettings() => null;
}