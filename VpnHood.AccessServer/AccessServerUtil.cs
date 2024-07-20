using System.Text;
using Renci.SshNet;

namespace VpnHood.AccessServer;

public static class AccessServerUtil
{
    public static string FindUniqueName(string template, string?[] names)
    {
        for (var i = 1;; i++) {
            var name = template.Replace("##", i.ToString());
            if (names.All(x => x != name))
                return name;
        }
    }

    public static async Task<string> ExecuteSshCommand(SshClient sshClient,
        string command, string? loginPassword, TimeSpan timeout)
    {
        command += ";echo 'CommandExecuted''!'"; // use ''!' to avoid matching with any other text
        await using var shellStream = sshClient.CreateShellStream("ShellStreamCommand", 0, 0, 0, 0, 2048);
        shellStream.WriteLine(command);
        await shellStream.FlushAsync();

        var result = new StringBuilder();
        var isCompleted = false;
        var passwordTried = false;
        while (!isCompleted) {
            // password prompt action
            var expectAction1 = new ExpectAction("password for ", str => {
                result.Append(str);

                if (string.IsNullOrEmpty(loginPassword))
                    throw new Exception("Login Password required!");

                if (passwordTried)
                    throw new Exception("Sorry, Server does not accept your login password!");

                // ReSharper disable once AccessToDisposedClosure
                shellStream.WriteLine(loginPassword);
                passwordTried = true;
            });

            var expectAction2 = new ExpectAction("CommandExecuted!", str => {
                result.Append(str);
                isCompleted = true;
            });

            shellStream.Expect(timeout, expectAction1, expectAction2);
        }

        return result.ToString();
    }
}