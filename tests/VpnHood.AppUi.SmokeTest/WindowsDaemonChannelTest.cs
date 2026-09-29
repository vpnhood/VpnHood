using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VpnHood.AppUi.Hosting.Cli.Channel;
using VpnHood.AppUi.Hosting.Cli.Windows;

namespace VpnHood.AppUi.SmokeTest;

// The service's pipe as Windows makes it. Elevated: a client trusts only a pipe owned by SYSTEM or
// Administrators, and HKLM is written.
[TestClass]
public class WindowsDaemonChannelTest
{
    private const string TestAppId = "com.vpnhood.test.channel";

    private static readonly DaemonChannelAnswer TestAnswer = new() {
        ApiUrl = new Uri("http://127.0.0.1:4711/?nocache=1#token=abc"),
        ProcessId = Environment.ProcessId,
        Version = "1.2.3"
    };

    [TestMethod]
    public async Task Pipe_is_recorded_first_and_its_own_and_answers_an_administrator()
    {
        RequireElevation();
        var paths = new WindowsCliPaths(TestAppId);
        var channel = new WindowsDaemonChannel(paths);

        try {
            await using var server = await Serve(channel);

            // recorded where a client looks
            var name = ReadRecorded(paths);
            Assert.IsNotNull(name, "the pipe's name is in HKLM");
            StringAssert.StartsWith(name, paths.InstanceName);

            // nobody else can be its first instance now
            Assert.Throws<Exception>(() =>
                NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.FirstPipeInstance, 4096, 4096, null).Dispose(),
                "a first instance under a served name");

            // a client gets through, trusts the owner, and reads the access list it was given
            await using var stream = await channel.Connect(CancellationToken.None);
            var pipe = (NamedPipeClientStream)stream;
            var rules = pipe.GetAccessControl().GetAccessRules(true, false, typeof(SecurityIdentifier)).OfType<PipeAccessRule>().ToArray();
            var users = rules.Single(x => ((SecurityIdentifier)x.IdentityReference).IsWellKnown(WellKnownSidType.AuthenticatedUserSid));
            Assert.AreEqual(PipeAccessRights.ReadWrite, users.PipeAccessRights & PipeAccessRights.ReadWrite, "read and write");
            Assert.AreEqual(0, (int)(users.PipeAccessRights & PipeAccessRights.CreateNewInstance), "never an instance");

            using var reader = new StreamReader(stream, leaveOpen: true);
            var received = await DaemonChannelClient.Ask(stream, reader, CancellationToken.None);
            Assert.IsNotNull(received);
            Assert.IsNull(received.Refusal, "an elevated caller is an administrator");
            Assert.AreEqual(TestAnswer.ApiUrl, received.ApiUrl);
            Assert.AreEqual("abc", received.Token);
            Assert.AreEqual(Environment.ProcessId, received.ProcessId);
        }
        finally {
            Registry.LocalMachine.DeleteSubKeyTree(paths.RegistryKeyPath, throwOnMissingSubKey: false);
        }
    }

    [TestMethod]
    public async Task A_pipe_someone_else_owns_is_not_trusted()
    {
        RequireElevation();
        var paths = new WindowsCliPaths(TestAppId);
        var name = $"{paths.InstanceName}-squatted-{Guid.NewGuid():N}";

        // made as a person would make it: owned by their own account
        using var identity = WindowsIdentity.GetCurrent();
        var security = new PipeSecurity();
        security.SetOwner(identity.User ?? throw new InvalidOperationException("The test runs as no user."));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        await using var squatter = NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security);

        try {
            using (var key = Registry.LocalMachine.CreateSubKey(paths.RegistryKeyPath))
                key.SetValue("DaemonChannel", name);

            var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                new WindowsDaemonChannel(paths).Connect(CancellationToken.None));
            StringAssert.Contains(error.Message, "not the service's");
        }
        finally {
            Registry.LocalMachine.DeleteSubKeyTree(paths.RegistryKeyPath, throwOnMissingSubKey: false);
        }
    }

    [TestMethod]
    public async Task Callers_that_leave_at_once_do_not_close_the_channel()
    {
        RequireElevation();
        var paths = new WindowsCliPaths(TestAppId);
        var channel = new WindowsDaemonChannel(paths);

        try {
            await using var server = await Serve(channel);
            var name = ReadRecorded(paths) ?? throw new InvalidOperationException("The pipe's name is not in HKLM.");

            // each may reach the next instance before the service waits on it, which breaks that instance
            for (var i = 0; i < 50; i++) {
                await using var pipe = new NamedPipeClientStream(".", name, PipeAccessRights.ReadWrite, PipeOptions.None,
                    TokenImpersonationLevel.Identification, HandleInheritability.None);
                pipe.Connect(5000);
            }

            var answer = await DaemonChannelClient.AskOnce(channel, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(Environment.ProcessId, answer.ProcessId, "an administrator is still answered");
        }
        finally {
            Registry.LocalMachine.DeleteSubKeyTree(paths.RegistryKeyPath, throwOnMissingSubKey: false);
        }
    }

    private static void RequireElevation()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            Assert.Inconclusive("Run elevated: the pipe's owner and HKLM need an administrator.");
    }

    private static async Task<DaemonChannelServer> Serve(WindowsDaemonChannel channel)
    {
        return await DaemonChannelServer.TryStart(channel, TestAnswer, "no", CancellationToken.None)
               ?? throw new InvalidOperationException("The channel could not be opened.");
    }

    private static string? ReadRecorded(WindowsCliPaths paths)
    {
        using var key = Registry.LocalMachine.OpenSubKey(paths.RegistryKeyPath);
        return key?.GetValue("DaemonChannel") as string;
    }
}
