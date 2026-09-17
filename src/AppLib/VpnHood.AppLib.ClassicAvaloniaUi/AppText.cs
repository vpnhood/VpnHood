using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Assets;
using VpnHood.AppLib.AvaloniaUI;
using VpnHood.AppLib.Api.Sessions;
using VpnHood.AppLib.Api.Settings;

namespace VpnHood.AppLib.ClassicAvaloniaUi;

// The app's state as a word on a row: what AppModel holds, said in the language showing. The words
// are this UI's, not the app's - another UI would say them its own way, or not at all - so they
// live here rather than in the read model every Avalonia UI shares.
public static class AppText
{
    public static string ProtocolTitle(ChannelProtocol protocol)
    {
        return protocol switch {
            ChannelProtocol.Udp => Strings.Current.ProtocolUdp,
            ChannelProtocol.Quic => Strings.Current.ProtocolQuic,
            _ => Strings.Current.ProtocolTcp
        };
    }

    // The home row's word for the countries split, from the EFFECTIVE mode in the state: a split
    // the toggle or the plan silenced reads Off (VpnHoodAppData.splitCountryStatusText).
    public const int AllCountriesCount = 238;
    private const int MaxFlags = 3;

    public static string SplitCountryStatusText(AppState state)
    {
        var split = state.SplitTunnelingState;
        switch (split.CountryMode) {
            case SplitCountryMode.ExcludeMyCountry:
                return Strings.Current.ExcludeMyCountry;
            case SplitCountryMode.ExcludeList: {
                var count = split.Countries.Count;
                if (count == 0) return Strings.Current.Off;
                if (count < MaxFlags) return Strings.Current.Exclude;
                if (count < AllCountriesCount / 2) return Strings.Current.AllExceptX(count);
                return Strings.Current.OnlyX(AllCountriesCount - count);
            }
            default:
                return Strings.Current.Off;
        }
    }

    public static string SplitAppsStatusText()
    {
        var split = AppModel.UserSettings.SplitTunneling;
        return split.AppMode switch {
            SplitAppMode.Exclude => split.Apps.Length > 0 ? Strings.Current.AllExceptX(split.Apps.Length) : Strings.Current.Off,
            SplitAppMode.Include => Strings.Current.OnlyX(split.Apps.Length),
            _ => Strings.Current.Off
        };
    }
}
