using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Win32;
using VpnHood.AppUi.Hosting.Desktop.Abstractions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppUi.Hosting.Desktop.Windows;

// The channel on Windows: a named pipe under a new random name each start, recorded in HKLM, since
// any user may take a fixed pipe name first. A client trusts only an owner a standard user cannot set.
public sealed class WindowsDaemonChannel(WindowsDesktopPaths paths) : IDaemonChannel
{
    private const string ChannelValueName = "DaemonChannel";
    private const int BufferSize = 4096;
    private const int ErrorNoData = unchecked((int)0x800700E8); // "The pipe is being closed."
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(500);

    public Task<IDaemonChannelListener> Listen(CancellationToken cancellationToken)
    {
        var name = $"{paths.InstanceName}-{RandomNumberGenerator.GetHexString(16, lowercase: true)}";
        var security = CreateSecurity();
        var first = Create(name, security, PipeOptions.FirstPipeInstance);
        try {
            Record(name); // once the pipe exists: a recorded name is a served one
        }
        catch {
            first.Dispose();
            throw;
        }

        return Task.FromResult<IDaemonChannelListener>(new Listener(this, name, security, first));
    }

    public async Task<Stream> Connect(CancellationToken cancellationToken)
    {
        var name = ReadRecorded() ??
                   throw new InvalidOperationException($"{paths.InstanceName} has not opened its channel.");

        // ReadWrite and no more: the two-way default asks for GENERIC_WRITE, which on a pipe includes
        // making an instance; and reading the owner below needs the permissions read ReadWrite holds.
        var client = new NamedPipeClientStream(".", name, PipeAccessRights.ReadWrite, PipeOptions.Asynchronous,
            TokenImpersonationLevel.Identification, HandleInheritability.None);
        try {
            await client.ConnectAsync((int)ConnectTimeout.TotalMilliseconds, cancellationToken).Vhc();
            var owner = client.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            if (owner == null || !IsTrustedOwner(owner))
                throw new InvalidOperationException(
                    $"The channel under {paths.InstanceName}'s name is not the service's: it is owned by {owner}.");

            return client;
        }
        catch {
            await client.DisposeAsync().Vhc();
            throw;
        }
    }

    // Anyone signed in reads and writes, so an administrator's unelevated window reaches the check;
    // only SYSTEM and Administrators may make an instance.
    private static PipeSecurity CreateSecurity()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return security;
    }

    private static NamedPipeServerStream Create(string name, PipeSecurity security, PipeOptions options)
    {
        return NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte, options | PipeOptions.Asynchronous, BufferSize, BufferSize, security);
    }

    private static bool IsTrustedOwner(SecurityIdentifier owner)
    {
        return owner.IsWellKnown(WellKnownSidType.LocalSystemSid) ||
               owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid);
    }

    private void Record(string name)
    {
        using var key = Registry.LocalMachine.CreateSubKey(paths.RegistryKeyPath);
        key.SetValue(ChannelValueName, name);
    }

    // Only this run's name: a newer start may have recorded its own by now.
    private void Forget(string name)
    {
        VhUtils.TryInvoke("forget the channel's name", () => {
            using var key = Registry.LocalMachine.OpenSubKey(paths.RegistryKeyPath, writable: true);
            if (key?.GetValue(ChannelValueName) as string == name)
                key.DeleteValue(ChannelValueName, throwOnMissingValue: false);
        });
    }

    private string? ReadRecorded()
    {
        using var key = Registry.LocalMachine.OpenSubKey(paths.RegistryKeyPath);
        return key?.GetValue(ChannelValueName) as string;
    }

    // One instance always waits, and the next is made before the last goes, so the name never lapses.
    private sealed class Listener(WindowsDaemonChannel channel, string name, PipeSecurity security,
        NamedPipeServerStream first) : IDaemonChannelListener
    {
        private NamedPipeServerStream? _waiting = first;

        public async Task<IDaemonChannelCaller> Accept(CancellationToken cancellationToken)
        {
            while (true) {
                // none waits only after the next could not be made
                var pipe = _waiting ??= Create(name, security, PipeOptions.None);
                try {
                    await pipe.WaitForConnectionAsync(cancellationToken).Vhc();
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested) {
                    // A broken instance is replaced. A caller that went before it was taken breaks
                    // one, and is common enough to be no failure.
                    _waiting = Create(name, security, PipeOptions.None);
                    await pipe.DisposeAsync().Vhc();
                    if (ex.HResult == ErrorNoData)
                        continue;

                    throw;
                }

                try {
                    _waiting = Create(name, security, PipeOptions.None);
                    return new Caller(pipe);
                }
                catch {
                    _waiting = null;
                    await pipe.DisposeAsync().Vhc();
                    throw;
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_waiting != null)
                await _waiting.DisposeAsync().Vhc();

            channel.Forget(name);
        }
    }

    // Who calls is read in the caller's own token, at the identification level the client allows.
    private sealed class Caller(NamedPipeServerStream pipe) : IDaemonChannelCaller
    {
        public Stream Stream => pipe;

        public DaemonCallerInfo Identify()
        {
            var name = "?";
            var isAdministrator = false;
            pipe.RunAsClient(() => {
                using var identity = WindowsIdentity.GetCurrent();
                name = identity.Name;
                isAdministrator = WindowsAdministrators.IsMember(identity);
            });

            return new DaemonCallerInfo(name, isAdministrator);
        }

        public ValueTask DisposeAsync() => pipe.DisposeAsync();
    }
}
