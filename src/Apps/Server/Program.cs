using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.App.Server;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using var serverApp = TryCreateServerApp();
        return serverApp != null
            ? await serverApp.Start(args, CancellationToken.None).Vhc()
            : 1;
    }

    // Settings that cannot be read are the operator's to mend, as a bad option is: one line that names
    // them and an exit code, as curl and git answer, rather than a crash that dumps core at each of
    // systemd's restarts. Any other failure still ends the process with its stack.
    private static ServerApp? TryCreateServerApp()
    {
        try {
            return new ServerApp();
        }
        catch (InvalidDataException ex) {
            Console.Error.WriteLine(ex.Message);
            return null;
        }
    }
}