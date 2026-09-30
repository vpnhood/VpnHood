using VpnHood.AppUi.Hosting.Cli.Exceptions;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Cli.Commands;

// "service log": the service reads its own log and sends it over the API, since its storage is
// administrators' only, and an unelevated window may not read it - nor would an elevated copy's
// output reach this terminal. With follow it asks every second and prints what is new, across a
// connect, and across a restart of the service too. When the service gives no log at all - the start
// that failed, when the log matters most - the platform shows what it keeps itself; a refusal is the
// answer, and stays one.
internal static class ServiceLog
{
    private static readonly TimeSpan FollowInterval = TimeSpan.FromSeconds(1);

    public static async Task<int> Run(CliPlatform platform, bool follow, int lines, CancellationToken cancellationToken)
    {
        try {
            await using var connection = await DaemonConnection.Open(platform, cancellationToken).Vhc();
            var follower = new ServiceLogFollower();
            Write(follower.First(await connection.Api.App.Log(cancellationToken).Vhc(), lines));
            if (follow)
                await Follow(connection, follower, cancellationToken).Vhc();

            return 0;
        }
        catch (OperationCanceledException) {
            return follow ? 0 : 130;
        }
        catch (DaemonRefusedException ex) {
            await Console.Error.WriteLineAsync(ex.Message).Vhc();
            return 1;
        }
        catch (Exception ex) {
            await Console.Error.WriteLineAsync(ex.Message).Vhc();
            return await platform.Instance.ShowOfflineLog(follow, lines, cancellationToken).Vhc();
        }
    }

    // Until Ctrl+C; a service that stops meanwhile is said so once, and followed again when it is back.
    private static async Task Follow(DaemonConnection connection, ServiceLogFollower follower,
        CancellationToken cancellationToken)
    {
        var isAnswering = true;
        while (true) {
            await Task.Delay(FollowInterval, cancellationToken).Vhc();
            try {
                Write(follower.Next(await connection.Api.App.Log(cancellationToken).Vhc()));
                isAnswering = true;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested) {
                if (isAnswering)
                    await Console.Error.WriteLineAsync(ex.Message).Vhc();

                isAnswering = false;
            }
        }
    }

    // Each log file begins with a byte order mark, which the answer carries into its middle.
    private static void Write(string text)
    {
        Console.Write(text.Replace("﻿", ""));
    }
}
