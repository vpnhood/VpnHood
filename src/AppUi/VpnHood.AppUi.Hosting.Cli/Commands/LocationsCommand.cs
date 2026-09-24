using System.CommandLine;
using VpnHood.AppUi.Hosting.Cli.Internal;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Cli.Commands;

// The locations a profile offers, so that "connect --location" can be typed by someone who cannot
// see the list the window would have shown them. The strings printed are the ones connect takes -
// ServerLocation, not the translated country name, which is there to read and not to type.
internal static class LocationsCommand
{
    // isAddAccessKeySupported false is a head with one built-in profile (Connect): --profile is not
    // offered, and the locations listed are always that profile's.
    public static Command Create(CliPlatform platform, bool isAddAccessKeySupported)
    {
        var profileOption = new Option<string?>("--profile", "-p") {
            Description = "The profile to list, by name or id. Defaults to the one the app is set to."
        };
        var jsonOption = new Option<bool>("--json") { Description = "Print the locations as JSON." };

        var command = new Command("locations", "List the server locations a profile offers.") { jsonOption };
        if (isAddAccessKeySupported)
            command.Options.Add(profileOption);

        command.SetAction((parseResult, cancellationToken) => DaemonSession.Run(platform, async (api, token) => {
            var info = await api.App.GetInfo(token).Vhc();
            var named = isAddAccessKeySupported ? parseResult.GetValue(profileOption) : null;
            var clientProfileId = ProfileLookup.Resolve(info, named, platform.Paths.CommandName);

            var profile = await api.ClientProfiles.Get(clientProfileId, token).Vhc();
            if (parseResult.GetValue(jsonOption)) {
                await CliPrinter.Json(profile.LocationInfos, token).Vhc();
                return 0;
            }

            var selected = profile.SelectedLocationInfo?.ServerLocation;

            // Measured, not guessed: "AU/New South Wales" is wider than any fixed column worth
            // having, and a name that runs into the one beside it is a list nobody can read.
            var width = profile.LocationInfos.Count == 0
                ? 0
                : profile.LocationInfos.Max(x => x.ServerLocation.Length);

            foreach (var location in profile.LocationInfos) {
                var marker = location.ServerLocation == selected ? "*" : " ";
                var name = location.IsAuto ? "Fastest" : location.CountryName;
                var region = string.IsNullOrEmpty(location.RegionName) ? "" : $" / {location.RegionName}";
                await CliPrinter.Line(
                    $"{marker} {location.ServerLocation.PadRight(width + 2)}{name}{region}", token).Vhc();
            }

            return 0;
        }, cancellationToken));

        return command;
    }
}
