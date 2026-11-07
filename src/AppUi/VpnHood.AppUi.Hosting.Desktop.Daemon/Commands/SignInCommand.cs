using System.CommandLine;
using System.Text;
using VpnHood.AppLib.Api;
using VpnHood.AppLib.Api.Accounts;
using VpnHood.AppUi.Hosting.Desktop.Daemon.Platform;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Desktop.Daemon.Commands;

// The account's email and password, as the window's form takes them, so a box with no window can use the plan
// its account holds: the account hands the app its code, which every connect sends. Signed in already, it says
// as whom and asks nothing, which is also how a person finds out.
//
// The password is never an argument, which a shell's history and ps would keep: it is asked for with nothing
// shown, or read from standard input, the first line, for a script. A second factor comes back as a challenge
// and is answered the same way, on the next line.
internal static class SignInCommand
{
    private const string PasswordProviderId = "password"; // AuthProviders.Password, the portal's own form

    public static Command Create(IDesktopPlatform platform)
    {
        var emailArgument = new Argument<string?>("email") {
            Description = "The account's email. None asks for it.",
            Arity = ArgumentArity.ZeroOrOne
        };

        var command = new Command("sign-in",
            "Sign in to your account. The password is asked for, or read from standard input.") {
            emailArgument
        };

        command.SetAction((parseResult, cancellationToken) => DaemonSession.Run(platform,
            (api, token) => Run(api, platform.Paths.CommandName, parseResult.GetValue(emailArgument), token),
            cancellationToken));

        return command;
    }

    public static Command CreateSignOut(IDesktopPlatform platform)
    {
        var command = new Command("sign-out", "Sign out of your account.");

        command.SetAction((_, cancellationToken) => DaemonSession.Run(platform, async (api, token) => {
            await api.Account.SignOut(token).Vhc();
            await CliPrinter.Line("Signed out.", token).Vhc();
            return 0;
        }, cancellationToken));

        return command;
    }

    private static async Task<int> Run(VpnHoodApi api, string commandName, string? email,
        CancellationToken cancellationToken)
    {
        var account = await api.Account.Get(cancellationToken).Vhc();
        if (account != null) {
            await CliPrinter.Line($"Signed in as {account.Email ?? account.UserId}. To change it: {commandName} sign-out",
                cancellationToken).Vhc();
            return 0;
        }

        email = string.IsNullOrWhiteSpace(email) ? ReadLine("Email: ") : email.Trim();
        var password = ReadSecret("Password: ");

        var result = await api.Account.SignIn(new SignInOptions {
            ProviderId = PasswordProviderId,
            UserName = email,
            Password = password
        }, cancellationToken).Vhc();

        if (result.State == SignInState.TotpRequired)
            result = await api.Account.SignIn(new SignInOptions {
                ProviderId = PasswordProviderId,
                TwoFactorCode = ReadLine("Code from your authenticator app, or a backup code: ")
            }, cancellationToken).Vhc();

        if (result.State != SignInState.SignedIn)
            throw new InvalidOperationException("The code was not accepted.");

        // a spent backup code's replacement, which nothing shows again
        if (result.NewBackupCode != null)
            await CliPrinter.Line($"Your new backup code, shown only now: {result.NewBackupCode}",
                cancellationToken).Vhc();

        // what brings the account's code onto the profile, for the next connect
        await api.Account.Refresh(cancellationToken).Vhc();
        account = await api.Account.Get(cancellationToken).Vhc();
        await CliPrinter.Line($"Signed in as {account?.Email ?? email}.", cancellationToken).Vhc();
        return 0;
    }

    // The prompts go to stderr, as sudo's do, so a script's stdout is the answer alone.
    private static string ReadLine(string prompt)
    {
        if (!Console.IsInputRedirected)
            Console.Error.Write(prompt);

        var line = Console.In.ReadLine()?.Trim();
        return string.IsNullOrEmpty(line)
            ? throw new InvalidOperationException($"Nothing was given for: {prompt.TrimEnd(' ', ':')}")
            : line;
    }

    private static string ReadSecret(string prompt)
    {
        if (Console.IsInputRedirected)
            return ReadLine(prompt);

        // The terminal stops echoing when a read starts, and the first read takes a moment to get there: a password
        // pasted as the prompt showed was drawn on the screen. Asking about a key sets the terminal up the same way.
        _ = Console.KeyAvailable;

        Console.Error.Write(prompt);
        var secret = new StringBuilder();
        while (true) {
            var key = Console.ReadKey(intercept: true);
            switch (key.Key) {
                case ConsoleKey.Enter:
                    Console.Error.WriteLine();
                    return secret.Length > 0
                        ? secret.ToString()
                        : throw new InvalidOperationException("Nothing was given for: Password");

                case ConsoleKey.Backspace:
                    if (secret.Length > 0)
                        secret.Length--;
                    break;

                default:
                    if (!char.IsControl(key.KeyChar))
                        secret.Append(key.KeyChar);
                    break;
            }
        }
    }
}
