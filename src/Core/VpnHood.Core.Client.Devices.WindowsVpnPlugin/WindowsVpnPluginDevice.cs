using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Devices.Abstractions;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;
using VpnHood.Net.Quic.MsQuic;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using Windows.ApplicationModel;
using Windows.Networking.Vpn;

namespace VpnHood.Core.Client.Devices.WindowsVpnPlugin;

// The app's side of the Windows VPN plug-in. Windows owns the virtual interface and launches the package's
// VPN host when this device connects its VpnPlugInProfile; the app reaches that host through the package's app
// service, which Windows runs in it.
public sealed class WindowsVpnPluginDevice : IDevice
{
    // Windows dials no plug-in profile without a server (RAS 749), though the plug-in holds the real
    // servers: TEST-NET-1, never routed, as on iOS.
    private const string PlaceholderServerAddress = "192.0.2.1";
    private const int InvalidHandleHResult = unchecked((int)0x80070006);

    // Windows routes the physical adapter's own DNS servers around the channel while it is connected, by host routes,
    // and the channel's host route for one it carries (WindowsVpnPluginAdapter) wins only by the interface metric:
    // Windows gives the VPN 55, above an Ethernet's 15, unless the profile's phonebook entry names one. No API sets
    // it; the entry is in the user's own phonebook (AddUserProfile), whose existing file the package changes in place,
    // unvirtualized.
    private const int InterfaceMetric = 1;
    private readonly string _profileName;

    // profileName: the profile's name in Windows' VPN settings and quick settings, the app's name
    public WindowsVpnPluginDevice(string profileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(profileName);
        _profileName = profileName;
        VpnServiceConfigFolder = WindowsVpnPluginFolders.VpnServiceFolder;
    }

    public string VpnServiceConfigFolder { get; }
    public string VpnServiceDataFolder => VpnServiceConfigFolder; // nothing backs up a desktop app's data
    public bool IsTv => false;
    public bool IsExcludeAppsSupported => false;
    public bool IsIncludeAppsSupported => false;
    public bool IsBindProcessToVpnSupported => false;
    public bool IsQuicSupported => MsQuicClient.IsSupported;

    // Not the app's: nothing of the package runs at boot, and the app ends with its window. Nor Windows': it dials no
    // plug-in profile again, its AlwaysOn set or not.
    public bool IsAlwaysOnSupported => false;
    public string OsInfo => Environment.OSVersion + ", " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");

    // as Chrome says it here, whose NT version reads 10.0 on Windows 11 too
    public DeviceUserAgentInfo UserAgentInfo { get; } = new() {
        Platform = $"Windows NT {Environment.OSVersion.Version.ToString(2)}" +
                   (Environment.Is64BitOperatingSystem ? "; Win64; x64" : ""),
        Browser = DeviceUserAgentInfo.GetChromeBrowser(isMobile: false)
    };

    public DeviceMemInfo MemInfo {
        get {
            var gcMemoryInfo = GC.GetGCMemoryInfo();
            return new DeviceMemInfo {
                TotalMemory = gcMemoryInfo.TotalAvailableMemoryBytes,
                AvailableMemory = gcMemoryInfo.TotalAvailableMemoryBytes - gcMemoryInfo.MemoryLoadBytes
            };
        }
    }

    public IReadOnlyList<DeviceAppInfo> InstalledApps =>
        throw new NotSupportedException("App filtering is not supported by the Windows VPN plug-in.");

    public async Task RequestVpnService(IUiContext? uiContext, TimeSpan timeout, CancellationToken cancellationToken)
    {
        _ = await GetOrCreateProfile(cancellationToken).Vhc();
    }

    public async Task StartVpnService(CancellationToken cancellationToken)
    {
        var profile = await GetOrCreateProfile(cancellationToken).Vhc();
        var agent = new VpnManagementAgent();

        // A plug-in profile can survive an app or package restart and reconnect with a stale service
        // configuration. Force a fresh activation after VpnServiceManager writes vpn.config.
        var connectionStatus = TryGetConnectionStatus(profile);
        if (connectionStatus != VpnManagementConnectionStatus.Disconnected) {
            if (connectionStatus is not null)
                await agent.DisconnectProfileAsync(profile).AsTask(cancellationToken).Vhc();

            await WaitForDisconnected(agent, cancellationToken).Vhc();
            profile = await GetOrCreateProfile(cancellationToken).Vhc();
        }

        SetInterfaceMetric();

        // A connect the VPN host took up ends in its status file, whose reason the manager reports - the server's
        // refusal, say - rather than Windows' bare status; one it never reached is Windows' own failure.
        var statusFilePath = Path.Combine(VpnServiceConfigFolder, ClientOptions.VpnStatusFileName);
        var connectTime = DateTime.UtcNow;
        var status = await agent.ConnectProfileAsync(profile).AsTask(cancellationToken).Vhc();
        if (status != VpnManagementErrorStatus.Ok && File.GetLastWriteTimeUtc(statusFilePath) < connectTime)
            throw new InvalidOperationException($"Windows could not connect the VPN profile. Status: {status}");
    }

    private async Task WaitForDisconnected(VpnManagementAgent agent, CancellationToken cancellationToken)
    {
        for (var retry = 0; retry < 50; retry++) {
            var profile = await FindProfile(agent, cancellationToken).Vhc();
            if (profile is null || TryGetConnectionStatus(profile) == VpnManagementConnectionStatus.Disconnected)
                return;

            await Task.Delay(200, cancellationToken).Vhc();
        }

        throw new TimeoutException("Windows did not disconnect the previous VPN plug-in session.");
    }

    // Before a dial, which reads the entry: a DNS server the physical adapter uses too is reached around the VPN
    // otherwise, so a failure says so, but the VPN still connects.
    private void SetInterfaceMetric()
    {
        var phonebookPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Network", "Connections", "Pbk", "rasphone.pbk");
        try {
            // Latin-1 maps each byte to one char and back, so the rest stays as Windows wrote it
            var text = File.ReadAllText(phonebookPath, Encoding.Latin1);
            var entry = Regex.Match(text, $@"(?ms)^\[{Regex.Escape(_profileName)}\]\r?$.*?(?=^\[|\z)");
            if (!entry.Success)
                throw new InvalidDataException("The phonebook has no entry for the VPN profile.");

            const string metricPattern = @"(?m)^(IpInterfaceMetric|Ipv6InterfaceMetric)=\d+";
            if (Regex.Matches(entry.Value, metricPattern).Count != 2)
                throw new InvalidDataException("The VPN profile's phonebook entry has no interface metric for each IP version.");

            var newEntry = Regex.Replace(entry.Value, metricPattern, $"$1={InterfaceMetric}");
            if (newEntry == entry.Value)
                return;

            File.WriteAllText(phonebookPath,
                text[..entry.Index] + newEntry + text[(entry.Index + entry.Length)..], Encoding.Latin1);
            VhLogger.Instance.LogInformation("The VPN profile's interface metric is set. Metric: {Metric}", InterfaceMetric);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex,
                "Could not set the VPN profile's interface metric, so a DNS server the physical adapter uses too is reached around the VPN.");
        }
    }

    // A profile listed while Windows was taking it out of a connection reads its status through that connection,
    // which fails once it is gone (0x80070006, the handle is invalid; the app's switch to another server hit it): not
    // known, so the next list tells.
    private static VpnManagementConnectionStatus? TryGetConnectionStatus(VpnPlugInProfile profile)
    {
        try {
            return profile.ConnectionStatus;
        }
        catch (COMException ex) when (ex.HResult == InvalidHandleHResult) {
            return null;
        }
    }

    // the agent lists the user's own phonebook's profiles and the package's
    private async Task<VpnPlugInProfile?> FindProfile(VpnManagementAgent agent, CancellationToken cancellationToken)
    {
        var profiles = await agent.GetProfilesAsync().AsTask(cancellationToken).Vhc();
        return profiles.OfType<VpnPlugInProfile>().FirstOrDefault(x =>
            string.Equals(x.ProfileName, _profileName, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<VpnPlugInProfile> GetOrCreateProfile(CancellationToken cancellationToken)
    {
        var agent = new VpnManagementAgent();
        var profile = await FindProfile(agent, cancellationToken).Vhc();

        var packageFamilyName = Package.Current.Id.FamilyName;
        if (profile is not null &&
            profile.ServerUris.Count > 0 &&
            string.Equals(profile.VpnPluginPackageFamilyName, packageFamilyName, StringComparison.OrdinalIgnoreCase))
            return profile;

        if (profile is not null) {
            await agent.DisconnectProfileAsync(profile).AsTask(cancellationToken).Vhc();
            // DeleteProfileAsync fails while Windows is still taking the profile out of the connected
            // state, so the disconnect settles first.
            await WaitForDisconnected(agent, cancellationToken).Vhc();
            await agent.DeleteProfileAsync(profile).AsTask(cancellationToken).Vhc();
        }

        await Task.Run(() => AddUserProfile(packageFamilyName), cancellationToken).Vhc();
        return await FindProfile(agent, cancellationToken).Vhc() ??
               throw new InvalidOperationException("Windows does not list the VPN profile it has just created.");
    }

    // As Add-VpnConnection makes a plug-in's, through WMI, whose provider runs out of the package: the profile is in the
    // user's own phonebook, the one Windows' VPN settings and quick settings list. The agent's add writes the package's
    // own, which they do not. Split tunneling, or Windows adds a default route of its own beside the plug-in's routes.
    // MSIX runs nothing at an uninstall, so the profile outlives the package, in Windows' settings until the person
    // removes it, and keeps the name once the agent lists it no more, its plug-in gone: a development package's, say,
    // which a Store install follows. Such a one is removed, as Remove-VpnConnection -Force does.
    private void AddUserProfile(string packageFamilyName)
    {
        using var vpnConnection = new ManagementClass(@"root\Microsoft\Windows\RemoteAccess\Client", "PS_VpnConnection", null);
        try {
            NewByThirdParty(vpnConnection, packageFamilyName);
        }
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.AlreadyExists) {
            VhLogger.Instance.LogInformation(
                "The VPN profile's name is held by a profile whose plug-in Windows does not list, which is removed.");
            using var parameters = vpnConnection.GetMethodParameters("Remove");
            parameters["Name"] = new[] { _profileName };
            parameters["Force"] = true;
            vpnConnection.InvokeMethod("Remove", parameters, null)?.Dispose();
            NewByThirdParty(vpnConnection, packageFamilyName);
        }
    }

    private void NewByThirdParty(ManagementClass vpnConnection, string packageFamilyName)
    {
        using var parameters = vpnConnection.GetMethodParameters("NewByThirdParty");
        parameters["Name"] = _profileName;
        parameters["ServerAddress"] = PlaceholderServerAddress;
        parameters["PlugInApplicationID"] = packageFamilyName;
        parameters["CustomConfiguration"] = "<VpnPlugin/>"; // XML, as the method takes it; the plug-in reads nothing of it
        parameters["SplitTunneling"] = true;
        parameters["RememberCredential"] = false;
        // a failure throws a ManagementException ("Object or property already exists"); a success answers nothing
        vpnConnection.InvokeMethod("NewByThirdParty", parameters, null)?.Dispose();
    }

    public IMessageClient CreateMessageClient() => new WindowsVpnPluginAppServiceClient();

    public void BindProcessToVpn(bool value)
    {
        if (value)
            throw new NotSupportedException("Binding the app's process to the VPN is not supported by the Windows VPN plug-in.");
    }

    public void Dispose()
    {
        // Windows owns the VPN host and the connection's lifetime.
    }
}
