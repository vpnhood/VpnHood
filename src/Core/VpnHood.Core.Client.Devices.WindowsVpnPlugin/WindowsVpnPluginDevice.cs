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
    // Windows dials no plug-in profile without ServerUris (RAS 749), though the plug-in holds the real
    // servers: TEST-NET-1, never routed, as on iOS.
    private const string PlaceholderServerUri = "https://192.0.2.1";
    private const int InvalidHandleHResult = unchecked((int)0x80070006);

    // Windows routes the physical adapter's own DNS servers around the channel while it is connected, by host routes,
    // and the channel's host route for one it carries (WindowsVpnPluginAdapter) wins only by the interface metric:
    // Windows gives the VPN 55, above an Ethernet's 15, unless the profile's phonebook entry names one. No API sets
    // it; the entry is in the package's LocalState, where Windows keeps the app's profiles.
    private const int InterfaceMetric = 1;
    private const string PhonebookFileName = "rasphone.pbk";
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
    public bool IsTcpProxySupported => true;
    public bool IsQuicSupported => MsQuicClient.IsSupported;

    // Windows keeps it, by the profile's own AlwaysOn: the app does not
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
            var profiles = await agent.GetProfilesAsync().AsTask(cancellationToken).Vhc();
            var profile = profiles.OfType<VpnPlugInProfile>().FirstOrDefault(x =>
                string.Equals(x.ProfileName, _profileName, StringComparison.OrdinalIgnoreCase));
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
        var phonebookPath = Path.Combine(WindowsVpnPluginFolders.LocalFolder, PhonebookFileName);
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

    private async Task<VpnPlugInProfile> GetOrCreateProfile(CancellationToken cancellationToken)
    {
        var agent = new VpnManagementAgent();
        var profiles = await agent.GetProfilesAsync().AsTask(cancellationToken).Vhc();
        var profile = profiles.OfType<VpnPlugInProfile>().FirstOrDefault(x =>
            string.Equals(x.ProfileName, _profileName, StringComparison.OrdinalIgnoreCase));

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

        profile = new VpnPlugInProfile {
            ProfileName = _profileName,
            VpnPluginPackageFamilyName = packageFamilyName,
            RequireVpnClientAppUI = false,
            AlwaysOn = false,
            RememberCredentials = false,
            // as the verified profile had it; the plug-in reads nothing from it
            CustomConfiguration = "VpnPlugin/1"
        };
        profile.ServerUris.Add(new Uri(PlaceholderServerUri));

        var status = await agent.AddProfileFromObjectAsync(profile).AsTask(cancellationToken).Vhc();
        if (status != VpnManagementErrorStatus.Ok)
            throw new InvalidOperationException($"Windows could not create the VPN profile. Status: {status}");

        return profile;
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
