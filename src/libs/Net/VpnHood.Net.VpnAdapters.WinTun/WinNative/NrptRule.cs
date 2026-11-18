using System.Net;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace VpnHood.Net.VpnAdapters.WinTun.WinNative;

// A rule of Windows' name resolution policy (NRPT) for every name, as Add-DnsClientNrptRule writes it: the DNS client
// asks only its servers, within a moment of the write. A list of Group Policy, where there is one, replaces every
// local rule, an empty list too, as a GPO leaves once its rules are removed (seen on Windows 11: the effective policy
// is empty while the local rule is there).
[SupportedOSPlatform("windows")]
internal static class NrptRule
{
    private const string LocalRulesPath = @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters\DnsPolicyConfig";
    private const string GroupPolicyRulesPath = @"SOFTWARE\Policies\Microsoft\Windows NT\DNSClient\DnsPolicyConfig";

    public static bool IsSetByGroupPolicy {
        get {
            using var key = Registry.LocalMachine.OpenSubKey(GroupPolicyRulesPath);
            return key != null;
        }
    }

    // Volatile, so one a crash left goes with the next boot, if no run replaces or removes it before
    public static void Set(Guid ruleId, IEnumerable<IPAddress> dnsServers, string comment)
    {
        const int version = 2;
        const int configOptionsGenericDnsServers = 8;

        using var key = Registry.LocalMachine.CreateSubKey($@"{LocalRulesPath}\{ruleId:B}", writable: true,
            RegistryOptions.Volatile);
        key.SetValue("Version", version, RegistryValueKind.DWord);
        key.SetValue("Name", new[] { "." }, RegistryValueKind.MultiString);
        key.SetValue("GenericDNSServers", string.Join(";", dnsServers), RegistryValueKind.String);
        key.SetValue("ConfigOptions", configOptionsGenericDnsServers, RegistryValueKind.DWord);
        key.SetValue("IPSECCARestriction", "", RegistryValueKind.String);
        key.SetValue("Comment", comment, RegistryValueKind.String);
    }

    public static void Remove(Guid ruleId)
    {
        Registry.LocalMachine.DeleteSubKeyTree($@"{LocalRulesPath}\{ruleId:B}", throwOnMissingSubKey: false);
    }
}
