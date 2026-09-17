using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Assets;
using VpnHood.AppLib.AvaloniaUI;
using VpnHood.AppLib.ClassicAvaloniaUi.Helpers;
using VpnHood.AppLib.Contracts.App;

namespace VpnHood.AppLib.ClassicAvaloniaUi.Views;

// The web UI's feature pages, each a FeaturePageLayout with its own words and picture: the pages
// under Settings that explain a device feature and open the device's settings for it
// (settings/notifications.vue and friends), the premium pitch of a feature this session has not
// bought, and the two with text of their own. The device's settings open through the app's
// intents, on the device that runs the app - a phone driving a TV opens them on the TV.
public static class FeaturePages
{
    private static Strings S => Strings.Current;

    public static FeaturePageView Notifications(MainView host)
    {
        return new FeaturePageView(host, new FeaturePageOptions {
            Title = S.AppNotificationsColored,
            Description = S.NotificationsDesc,
            Image = "notifications.webp",
            Steps = [S.NotificationHowToTurnOnStep1, S.NotificationHowToTurnOnStep2],
            ButtonText = AppModel.IsNotificationEnabled(AppModel.State) ? S.TurnOffNotification : S.TurnOnNotification,
            Action = () => AppModel.Api.Intents.OpenAppNotificationSettings(CancellationToken.None),
            IsActionAvailable = AppModel.Intents.IsAppNotificationSettingsSupported
        });
    }

    public static FeaturePageView QuickLaunch(MainView host)
    {
        // the prompt is answered by opening the page; a second visit is the person's own
        var settings = AppModel.UserSettings;
        var showSkip = AppModel.State.IsQuickLaunchRecommended;
        if (!settings.IsQuickLaunchPrompted) {
            settings.IsQuickLaunchPrompted = true;
            AppModel.SaveUserSettings(settings, CancellationToken.None).Forget("Could not save that quick launch was offered.");
        }

        var appName = AppModel.Features.AppName;
        var isRequestSupported = AppModel.Intents.IsRequestQuickLaunchSupported;
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
                var added = await AppModel.Api.Intents.RequestQuickLaunch(CancellationToken.None);
                if (added)
                    host.GoBack();
            },
            IsPremium = AppModel.IsPremiumFeature(AppFeature.QuickLaunch),
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
            Steps = [S.KillSwitchHowToTurnOnStep1, S.KillSwitchHowToTurnOnStep2(AppModel.Features.AppName, "⚙️"), S.KillSwitchHowToTurnOnStep3],
            ButtonText = S.OpenVpnSettings,
            Action = () => AppModel.Api.Intents.OpenKillSwitchSettings(CancellationToken.None),
            IsActionAvailable = AppModel.Intents.IsKillSwitchSettingsSupported
        });
    }

    public static FeaturePageView AlwaysOn(MainView host)
    {
        return new FeaturePageView(host, new FeaturePageOptions {
            Title = S.AlwaysOnColored,
            Description = S.AlwaysOnDesc,
            Image = "always-on.webp",
            Steps = [S.AlwaysOnHowToTurnOnStep1, S.AlwaysOnHowToTurnOnStep2(AppModel.Features.AppName, "⚙️"), S.AlwaysOnHowToTurnOnStep3],
            ButtonText = S.OpenVpnSettings,
            Action = () => AppModel.Api.Intents.OpenAlwaysOnSettings(CancellationToken.None),
            IsPremium = AppModel.IsPremiumFeature(AppFeature.AlwaysOn),
            IsActionAvailable = AppModel.Intents.IsAlwaysOnSettingsSupported
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
            Action = () => AppModel.Api.Intents.OpenSettings(CancellationToken.None),
            IsPremium = AppModel.IsPremiumFeature(AppFeature.CustomDns),
            IsActionAvailable = AppModel.Intents.IsPrivateDnsSettingsSupported
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
        var settings = AppModel.UserSettings;
        if (!settings.IsTcpProxyPrompted) {
            settings.IsTcpProxyPrompted = true;
            AppModel.SaveUserSettings(settings, CancellationToken.None).Forget("Could not save that cloak mode was offered.");
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
            IsPremium = AppModel.IsPremiumFeature(feature)
        });
    }
}
