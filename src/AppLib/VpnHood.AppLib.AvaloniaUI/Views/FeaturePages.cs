using VpnHood.AppLib.AvaloniaUI.Resources;
using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppLib.AvaloniaUI.Views;

// The web UI's feature pages, each a FeaturePageLayout with its own words and picture: the pages
// under Settings that explain a device feature and open the device's settings for it
// (settings/notifications.vue and friends), the premium pitch of a feature this session has not
// bought, and the two with text of their own.
public static class FeaturePages
{
    private static VpnHoodApp App => VpnHoodApp.Instance;
    private static Strings S => Strings.Current;

    public static FeaturePageView Notifications(MainView host)
    {
        return new FeaturePageView(host, new FeaturePageOptions {
            Title = S.AppNotificationsColored,
            Description = S.NotificationsDesc,
            Image = "notifications.webp",
            Steps = [S.NotificationHowToTurnOnStep1, S.NotificationHowToTurnOnStep2],
            ButtonText = AppData.IsNotificationEnabled(AppData.State) ? S.TurnOffNotification : S.TurnOnNotification,
            Action = () => {
                App.Services.DeviceUiProvider.OpenAppNotificationSettings(AppUiContext.RequiredContext);
                return Task.CompletedTask;
            },
            IsActionAvailable = AppData.Intents.IsAppNotificationSettingsSupported
        });
    }

    public static FeaturePageView QuickLaunch(MainView host)
    {
        // the prompt is answered by opening the page; a second visit is the person's own
        var settings = App.UserSettings;
        var showSkip = AppData.State.IsQuickLaunchRecommended;
        if (!settings.IsQuickLaunchPrompted) {
            settings.IsQuickLaunchPrompted = true;
            App.SettingsService.Save();
        }

        var appName = App.Features.AppName;
        var isRequestSupported = AppData.Intents.IsRequestQuickLaunchSupported;
        return new FeaturePageView(host, new FeaturePageOptions {
            Title = S.QuickLaunchColored,
            Description = S.QuickLaunchDesc,
            Image = "quick-launch.webp",
            Steps = isRequestSupported
                ? [S.QuickLaunchHowToTurnOnStep1, S.QuickLaunchHowToTurnOnStep2]
                : [
                    S.QuickLaunchHowToTurnOnAlternativeStep1, S.QuickLaunchHowToTurnOnAlternativeStep2,
                    S.QuickLaunchHowToTurnOnAlternativeStep3(appName), S.QuickLaunchHowToTurnOnAlternativeStep4,
                    S.QuickLaunchHowToTurnOnAlternativeStep5
                ],
            ButtonText = S.QuickLaunchTurnOn,
            Action = async () => {
                var added = await App.Services.DeviceUiProvider.RequestQuickLaunch(AppUiContext.RequiredContext, CancellationToken.None);
                if (added)
                    host.GoBack();
            },
            IsPremium = AppData.IsPremiumFeature(AppFeature.QuickLaunch),
            IsActionAvailable = isRequestSupported,
            ShowSkip = showSkip
        });
    }

    public static FeaturePageView KillSwitch(MainView host)
    {
        return new FeaturePageView(host, new FeaturePageOptions {
            Title = S.KillSwitchColored,
            Description = S.KillSwitchDesc,
            Image = "kill-switch.webp",
            Steps = [S.KillSwitchHowToTurnOnStep1, S.KillSwitchHowToTurnOnStep2(App.Features.AppName, "⚙️"), S.KillSwitchHowToTurnOnStep3],
            ButtonText = S.OpenVpnSettings,
            Action = () => {
                App.Services.DeviceUiProvider.OpenKillSwitchSettings(AppUiContext.RequiredContext);
                return Task.CompletedTask;
            },
            IsActionAvailable = AppData.Intents.IsKillSwitchSettingsSupported
        });
    }

    public static FeaturePageView AlwaysOn(MainView host)
    {
        return new FeaturePageView(host, new FeaturePageOptions {
            Title = S.AlwaysOnColored,
            Description = S.AlwaysOnDesc,
            Image = "always-on.webp",
            Steps = [S.AlwaysOnHowToTurnOnStep1, S.AlwaysOnHowToTurnOnStep2(App.Features.AppName, "⚙️"), S.AlwaysOnHowToTurnOnStep3],
            ButtonText = S.OpenVpnSettings,
            Action = () => {
                App.Services.DeviceUiProvider.OpenAlwaysOnSettings(AppUiContext.RequiredContext);
                return Task.CompletedTask;
            },
            IsPremium = AppData.IsPremiumFeature(AppFeature.AlwaysOn),
            IsActionAvailable = AppData.Intents.IsAlwaysOnSettingsSupported
        });
    }

    public static FeaturePageView PrivateDns(MainView host)
    {
        return new FeaturePageView(host, new FeaturePageOptions {
            Title = S.PrivateDnsColored,
            Description = S.PrivateDnsDesc,
            Image = "private-dns.webp",
            Steps = [S.PrivateDnsTurnOnStep1, S.PrivateDnsTurnOnStep2, S.PrivateDnsTurnOnStep3, S.PrivateDnsTurnOnStep4, S.PrivateDnsTurnOnStep5],
            ButtonText = S.OpenSystemSettings,
            Action = () => {
                App.Services.DeviceUiProvider.OpenSettings(AppUiContext.RequiredContext);
                return Task.CompletedTask;
            },
            IsPremium = AppData.IsPremiumFeature(AppFeature.CustomDns),
            IsActionAvailable = AppData.Intents.IsPrivateDnsSettingsSupported
        });
    }

    public static FeaturePageView PrivateDnsError(MainView host)
    {
        return new FeaturePageView(host, new FeaturePageOptions {
            Title = S.PrivateDnsColored,
            Description = S.PrivateDnsDialogMsg,
            Image = "private-dns.webp",
            Kind = FeaturePageKind.PrivateDnsError
        });
    }

    public static FeaturePageView CloakMode(MainView host)
    {
        var settings = App.UserSettings;
        if (!settings.IsTcpProxyPrompted) {
            settings.IsTcpProxyPrompted = true;
            App.SettingsService.Save();
        }

        return new FeaturePageView(host, new FeaturePageOptions {
            Title = S.CloakModeColored,
            Image = "cloak-mode.webp",
            Kind = FeaturePageKind.CloakMode
        });
    }

    // the pitch a sold feature shows in place of its page while this session has not bought it
    public static FeaturePageView PremiumPitch(MainView host, string title, string description, string image, AppFeature feature)
    {
        return new FeaturePageView(host, new FeaturePageOptions {
            Title = title,
            Description = description,
            Image = image,
            IsPremium = AppData.IsPremiumFeature(feature)
        });
    }
}
