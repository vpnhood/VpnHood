using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.Settings;
using VpnHood.AppUi.Common;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Helpers;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views;

// The feature pages, each a FeaturePageView with its own words and picture: the pages
// under Settings that explain a device feature and open the device's settings for it
// (notifications and the like), the premium pitch of a feature this session has not
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
            ButtonText = VhApp.IsNotificationEnabled(VhApp.State) ? S.TurnOffNotification : S.TurnOnNotification,
            Action = () => VhApp.Api.Intents.OpenAppNotificationSettings(CancellationToken.None),
            IsActionAvailable = VhApp.Intents.IsAppNotificationSettingsSupported
        });
    }

    public static FeaturePageView QuickLaunch(MainView host)
    {
        // the prompt is answered by opening the page; a second visit is the person's own
        var settings = VhApp.UserSettings;
        var showSkip = VhApp.State.IsQuickLaunchRecommended;
        if (!settings.IsQuickLaunchPrompted) {
            settings.IsQuickLaunchPrompted = true;
            VhApp.SaveUserSettings(settings, CancellationToken.None).Forget("Could not save that quick launch was offered.");
        }

        var appName = VhApp.Descriptor.AppName;
        var isRequestSupported = VhApp.Intents.IsRequestQuickLaunchSupported;
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
                var added = await VhApp.Api.Intents.RequestQuickLaunch(CancellationToken.None);
                if (added)
                    host.GoBack();
            },
            IsPremium = VhApp.IsPremiumFeature(AppFeature.QuickLaunch),
            IsActionAvailable = isRequestSupported,
            ShowSkip = showSkip
        });
    }

    // The installers' own words for the way out, in English as they are (install-client.nsi, install-client.sh):
    // the page names them as a person finds them.
    private const string KillSwitchOffEntryName = "Turn off kill switch";

    // the app's own levels where the app keeps a kill switch itself (a desktop's), with the way out a person needs
    // if the app cannot open while the internet is blocked; else the steps to the device's setting (Android's). No button for the way out: the window reads
    // everything from the service, so it does not open while the service is down. Linux's names the command too,
    // as the right-click action is there only where pkexec is.
    public static FeaturePageView KillSwitch(MainView host)
    {
        var features = VhApp.Descriptor;
        if (features.IsKillSwitchSupported) {
            var appName = features.AppName;
            return new FeaturePageView(host, new FeaturePageOptions {
                Title = S.KillSwitchColored,
                Description = S.KillSwitchDesc,
                Image = "kill-switch.webp",
                Kind = FeaturePageKind.Choice,
                Choices = [
                    KillSwitchChoice(KillSwitchMode.Off, S.Off, S.KillSwitchOffDesc),
                    KillSwitchChoice(KillSwitchMode.UntilDisconnect, S.KillSwitchUntilDisconnect,
                        S.KillSwitchUntilDisconnectDesc),
                    KillSwitchChoice(KillSwitchMode.Lockdown, S.KillSwitchLockdown, S.KillSwitchLockdownDesc)
                ],
                ChoiceNote = features.OsType switch {
                    AppOsType.Windows => S.KillSwitchWayOutWindows(appName, $"{appName} - {KillSwitchOffEntryName}"),
                    AppOsType.Linux when features.KillSwitchOffScriptPath != null =>
                        S.KillSwitchWayOutLinux(appName, KillSwitchOffEntryName, $"sudo {features.KillSwitchOffScriptPath}"),
                    _ => null
                },
                IsPremium = VhApp.IsPremiumFeature(AppFeature.KillSwitch)
            });
        }

        return new FeaturePageView(host, new FeaturePageOptions {
            Title = S.KillSwitchColored,
            Description = S.KillSwitchDesc,
            Image = "kill-switch.webp",
            Steps = [S.KillSwitchHowToTurnOnStep1, S.KillSwitchHowToTurnOnStep2(VhApp.Descriptor.AppName, "⚙️"), S.KillSwitchHowToTurnOnStep3],
            ButtonText = S.OpenVpnSettings,
            Action = () => VhApp.Api.Intents.OpenKillSwitchSettings(CancellationToken.None),
            IsActionAvailable = VhApp.Intents.IsKillSwitchSettingsSupported
        });
    }

    private static FeaturePageChoice KillSwitchChoice(KillSwitchMode mode, string title, string? description)
    {
        return new FeaturePageChoice {
            Title = title,
            Description = description,
            IsChosen = VhApp.UserSettings.KillSwitch == mode,
            Action = () => SetKillSwitch(mode, CancellationToken.None)
        };
    }

    private static async Task SetKillSwitch(KillSwitchMode mode, CancellationToken cancellationToken)
    {
        var settings = VhApp.UserSettings;
        settings.KillSwitch = mode;
        await VhApp.SaveUserSettings(settings, cancellationToken);
    }

    // the app's own switch where the app keeps always on itself (a desktop's service); else the steps to
    // the device's setting (Android's)
    public static FeaturePageView AlwaysOn(MainView host)
    {
        var alwaysOnSupport = VhApp.Descriptor.AlwaysOnSupport;
        if (alwaysOnSupport == AlwaysOnSupport.App) {
            return new FeaturePageView(host, new FeaturePageOptions {
                Title = S.AlwaysOnColored,
                Description = S.AlwaysOnDesc,
                Image = "always-on.webp",
                Kind = FeaturePageKind.Switch,
                SwitchTitle = S.AlwaysOn,
                IsSwitchOn = VhApp.UserSettings.AlwaysOn,
                SwitchAction = isOn => SetAlwaysOn(isOn, CancellationToken.None),
                IsPremium = VhApp.IsPremiumFeature(AppFeature.AlwaysOn)
            });
        }

        return new FeaturePageView(host, new FeaturePageOptions {
            Title = S.AlwaysOnColored,
            Description = S.AlwaysOnDesc,
            Image = "always-on.webp",
            Steps = [S.AlwaysOnHowToTurnOnStep1, S.AlwaysOnHowToTurnOnStep2(VhApp.Descriptor.AppName, "⚙️"), S.AlwaysOnHowToTurnOnStep3],
            ButtonText = S.OpenVpnSettings,
            Action = () => VhApp.Api.Intents.OpenAlwaysOnSettings(CancellationToken.None),
            IsPremium = VhApp.IsPremiumFeature(AppFeature.AlwaysOn),
            IsActionAvailable = alwaysOnSupport == AlwaysOnSupport.Device
        });
    }

    private static async Task SetAlwaysOn(bool isOn, CancellationToken cancellationToken)
    {
        var settings = VhApp.UserSettings;
        settings.AlwaysOn = isOn;
        await VhApp.SaveUserSettings(settings, cancellationToken);
    }

    public static FeaturePageView PrivateDns(MainView host)
    {
        return new FeaturePageView(host, new FeaturePageOptions {
            Title = S.PrivateDnsColored,
            Description = S.PrivateDnsDesc,
            Image = "private-dns.webp",
            Steps = [S.PrivateDnsTurnOnStep1, S.PrivateDnsTurnOnStep2, S.PrivateDnsTurnOnStep3, S.PrivateDnsTurnOnStep4, S.PrivateDnsTurnOnStep5],
            ButtonText = S.OpenSystemSettings,
            Action = () => VhApp.Api.Intents.OpenSettings(CancellationToken.None),
            IsPremium = VhApp.IsPremiumFeature(AppFeature.CustomDns),
            IsActionAvailable = VhApp.Intents.IsPrivateDnsSettingsSupported
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

    public static FeaturePageView CloakMode(MainView host, bool isPrompt)
    {
        var settings = VhApp.UserSettings;
        if (!settings.IsTcpForwardingPrompted) {
            settings.IsTcpForwardingPrompted = true;
            VhApp.SaveUserSettings(settings, CancellationToken.None).Forget("Could not save that cloak mode was offered.");
        }

        return new FeaturePageView(host, new FeaturePageOptions {
            Title = S.CloakModeColored,
            Image = "cloak-mode.webp",
            IsPrompt = isPrompt,
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
            IsPremium = VhApp.IsPremiumFeature(feature)
        });
    }
}
