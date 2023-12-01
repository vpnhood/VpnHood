using System.Net;
using System.Text;
using Renci.SshNet;

namespace VpnHood.AccessServer;

public static class AccessServerUtil
{
    public static string ValidateIpEndPoint(string ipEndPoint)
    {
        return IPEndPoint.Parse(ipEndPoint).ToString();
    }
        
    public static string ValidateIpAddress(string ipAddress)
    {
        return IPAddress.Parse(ipAddress).ToString();
    }
    public static string FindUniqueName(string template, string?[] names)
    {
        for (var i = 2; ; i++)
        {
            var name = template.Replace("##", i.ToString());
            if (names.All(x => x != name))
                return name;
        }
    }

    public static string? GenerateCacheKey(string keyBase, DateTime? beginTime, DateTime? endTime, out TimeSpan? cacheExpiration)
    {
        cacheExpiration = null;
        if (endTime != null && DateTime.UtcNow - endTime >= TimeSpan.FromMinutes(5)) 
            return null;

        var duration = (endTime ?? DateTime.UtcNow) - (beginTime ?? DateTime.UtcNow.AddYears(-2));
        var threshold = (long) (duration.TotalMinutes / 30);
        var cacheKey = $"{keyBase}_{threshold}";
        cacheExpiration = TimeSpan.FromMinutes(Math.Min(60 * 24, duration.TotalMinutes / 30));
        return cacheKey;
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
        while (!isCompleted)
        {
            // password prompt action
            var expectAction1 = new ExpectAction("password for ", str =>
            {
                result.Append(str);

                if (string.IsNullOrEmpty(loginPassword))
                    throw new Exception("Login Password required!");

                if (passwordTried)
                    throw new Exception("Sorry, Server does not accept your login password!");

                // ReSharper disable once AccessToDisposedClosure
                shellStream.WriteLine(loginPassword);
                passwordTried = true;
            });

            var expectAction2 = new ExpectAction("CommandExecuted!", str =>
            {
                result.Append(str);
                isCompleted = true;
            });

            shellStream.Expect(timeout, expectAction1, expectAction2);
        }

        return result.ToString();
    }
}