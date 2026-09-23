using VpnHood.AppLib.Api;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Cli.Internal;

// What every command that talks to the daemon does around its one interesting line: dial, run,
// hang up, and turn a failure into a sentence. Here once rather than in eight command bodies, so
// each of those is the call it makes and nothing else.
//
// A command's exit code is its own; anything thrown becomes 1 and a line on stderr. The message is
// the exception's own - the API rebuilds the app's exceptions from what the daemon sent, so a bad
// access key says what it said in the app.
internal static class DaemonSession
{
    public static async Task<int> Run(CliPlatform platform,
        Func<VpnHoodApi, CancellationToken, Task<int>> body, CancellationToken cancellationToken)
    {
        try {
            using var connection = await DaemonConnection.Open(platform, cancellationToken).Vhc();
            return await body(connection.Api, cancellationToken).Vhc();
        }
        catch (OperationCanceledException) {
            return 130; // the shell's own code for a command a person interrupted
        }
        catch (Exception ex) {
            await Console.Error.WriteLineAsync(ex.Message).Vhc();
            return 1;
        }
    }
}
