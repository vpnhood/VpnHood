using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Net.VpnAdapters.LinuxTun;

namespace VpnHood.Core.Client.Devices.Linux;

// The kill switch's block on Linux: an nftables table of ours for both families (inet), replaced whole by one
// `nft -f`, a transaction with no gap. It sits beside ufw's, firewalld's and Docker's tables, and a drop in any
// table wins, so ours only adds drops. The output chain lets out the tunnel's interface by name (first, so a
// tunnel packet costs one compare: the kernel runs the table for every outbound packet while the switch is on,
// which the owner accepted for the switch, 2026-10-31), loopback, the service by its cgroup, DHCP and NDP, an
// out-of-tunnel plan's resolvers, then blocks DNS - a LAN router's too - then lets out the device-level split and
// blocks the rest. A blocked TCP connect is reset, so it fails at once: of a dropped SYN the kernel only sends
// another, and the app hangs till its own timeout (seen on Ubuntu 26.04); a dropped UDP send fails at once
// (EPERM). The forward chain does the same for forwarded traffic (Docker, libvirt), which would otherwise go
// around it.
// The kernel keeps the table through a crash, not a reboot, so a block also goes to a file that a boot unit of
// its own loads before the network comes up (the block holds through a reboot, owner, 2026-10-31). That copy
// leaves the service's rule out: nft resolves a cgroup when it loads the rule, and the service's is not there
// yet; the service is let out again at its next block. The names are the SDK's, so every app built on VpnHood
// shares the one block; the table's comment names the app whose block it is (owner: the app's own storage
// folder, which an update keeps). Needs root: the service.
public sealed class LinuxVpnKillSwitch(string owner) : IVpnKillSwitch
{
    private const string TableName = "vpnhood_kill_switch";
    private const string RunFolder = "/run/vpnhood-kill-switch";
    private const string BootFolder = "/var/lib/vpnhood-kill-switch";
    private const string BootUnitName = "vpnhood-kill-switch.service";
    private static readonly string ApplyFilePath = Path.Combine(RunFolder, "apply.nft");
    private static readonly string BootFilePath = Path.Combine(BootFolder, "block.nft");
    private static readonly string BootUnitPath = Path.Combine("/etc/systemd/system", BootUnitName);
    private static readonly string[] NftPaths = ["/usr/sbin/nft", "/sbin/nft", "/usr/bin/nft", "/bin/nft"];
    private const UnixFileMode RootOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly AsyncLock _lock = new();

    // a table's comment is short, so the owner goes in as a hash
    private readonly string _ownerTag =
        "app " + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(owner)))[..16];

    public async Task<bool> IsBlocking(CancellationToken cancellationToken)
    {
        using var scope = await _lock.LockAsync(cancellationToken).Vhc();
        return await GetOwnerTag(cancellationToken).Vhc() == _ownerTag;
    }

    public async Task Block(VpnKillSwitchRules rules, CancellationToken cancellationToken)
    {
        var nftPath = FindNft() ??
                      throw new NotSupportedException("The kill switch needs nftables (nft), which is not installed.");
        var cgroupPath = await GetCgroupPath(cancellationToken).Vhc();
        var tunName = LinuxTunVpnAdapter.GetValidAdapterName(rules.AdapterName);

        using var scope = await _lock.LockAsync(cancellationToken).Vhc();
        Directory.CreateDirectory(RunFolder, RootOnly);
        await FileUtils.WriteAllTextAtomicAsync(ApplyFilePath, CreateRuleset(rules, tunName, cgroupPath),
            cancellationToken).Vhc();
        await OsUtils.ExecuteCommandAsync(nftPath, $"-f {ApplyFilePath}", cancellationToken).Vhc();

        Directory.CreateDirectory(BootFolder, RootOnly);
        await FileUtils.WriteAllTextAtomicAsync(BootFilePath, CreateRuleset(rules, tunName, cgroupPath: null),
            cancellationToken).Vhc();
        await EnsureBootUnit(nftPath, cancellationToken).Vhc();

        VhLogger.Instance.LogInformation(
            "The kill switch's block is up. Tunnel: {Tunnel}, PassIpRanges: {PassIpRanges}, Cgroup: {Cgroup}",
            tunName, rules.PassIpRanges.Count, cgroupPath);
    }

    public async Task Unblock(bool anyApp, CancellationToken cancellationToken)
    {
        // only root reads or changes it, and a block a process of a person's could not have put up is no
        // block of its own: nothing to lift
        if (!Environment.IsPrivilegedProcess)
            return;

        using var scope = await _lock.LockAsync(cancellationToken).Vhc();
        var ownerTag = await GetOwnerTag(cancellationToken).Vhc();
        if (ownerTag == null || (!anyApp && ownerTag != _ownerTag))
            return;

        if (FindNft() is { } nftPath) {
            Directory.CreateDirectory(RunFolder, RootOnly);
            await FileUtils.WriteAllTextAtomicAsync(ApplyFilePath,
                $"table inet {TableName}\ndelete table inet {TableName}\n", cancellationToken).Vhc();
            await OsUtils.ExecuteCommandAsync(nftPath, $"-f {ApplyFilePath}", cancellationToken).Vhc();
        }

        File.Delete(BootFilePath);
        VhLogger.Instance.LogInformation("The kill switch's block is lifted. Its app: {Owner}",
            ownerTag == _ownerTag ? "this" : "another");
    }

    // The tag of the app whose block is up, or null where none is: the kernel's table, or after a reboot whose
    // boot unit has not loaded it, the boot copy.
    private static async Task<string?> GetOwnerTag(CancellationToken cancellationToken)
    {
        if (!Environment.IsPrivilegedProcess)
            return null;

        var ruleset = await ReadTable(cancellationToken).Vhc();
        if (ruleset == null && File.Exists(BootFilePath))
            ruleset = await File.ReadAllTextAsync(BootFilePath, cancellationToken).Vhc();

        if (ruleset == null)
            return null;

        var match = Regex.Match(ruleset, "comment \"(app [0-9a-f]+)\"");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static async Task<string?> ReadTable(CancellationToken cancellationToken)
    {
        if (FindNft() is not { } nftPath)
            return null;

        var tables = await OsUtils.ExecuteCommandAsync(nftPath, "list tables", cancellationToken).Vhc();
        if (!tables.Split('\n').Any(x => x.Trim() == $"table inet {TableName}"))
            return null;

        return await OsUtils.ExecuteCommandAsync(nftPath, $"list table inet {TableName}", cancellationToken).Vhc();
    }

    // What the table holds; the old one goes in the same transaction, which a missing one does not fail.
    private string CreateRuleset(VpnKillSwitchRules rules, string tunName, string? cgroupPath)
    {
        var passV4 = rules.PassIpRanges.Where(x => x.AddressFamily == AddressFamily.InterNetwork).ToArray();
        var passV6 = rules.PassIpRanges.Where(x => x.AddressFamily == AddressFamily.InterNetworkV6).ToArray();
        var dnsV4 = rules.DnsServers.Where(x => x.AddressFamily == AddressFamily.InterNetwork).ToArray();
        var dnsV6 = rules.DnsServers.Where(x => x.AddressFamily == AddressFamily.InterNetworkV6).ToArray();

        var builder = new StringBuilder();
        builder.Append($"table inet {TableName}\n");
        builder.Append($"delete table inet {TableName}\n");
        builder.Append($"table inet {TableName} {{\n");
        builder.Append($"  comment \"{_ownerTag}\"\n");
        AppendSet(builder, "pass_v4", "ipv4_addr", passV4.Select(FormatRange));
        AppendSet(builder, "pass_v6", "ipv6_addr", passV6.Select(FormatRange));
        AppendSet(builder, "dns_v4", "ipv4_addr", dnsV4.Select(x => x.ToString()));
        AppendSet(builder, "dns_v6", "ipv6_addr", dnsV6.Select(x => x.ToString()));

        builder.Append("  chain output {\n");
        builder.Append("    type filter hook output priority filter; policy accept;\n");
        builder.Append($"    oifname \"{tunName}\" accept\n");
        builder.Append("    oifname \"lo\" accept\n");
        if (cgroupPath != null)
            builder.Append($"    socket cgroupv2 level {cgroupPath.Split('/').Length} \"{cgroupPath}\" accept\n");
        builder.Append("    udp sport 68 udp dport 67 accept\n");
        builder.Append("    udp sport 546 udp dport 547 accept\n");
        builder.Append("    icmpv6 type { nd-router-solicit, nd-router-advert, nd-neighbor-solicit, " +
                       "nd-neighbor-advert, nd-redirect } accept\n");
        if (dnsV4.Length > 0)
            builder.Append("    meta l4proto { tcp, udp } th dport 53 ip daddr @dns_v4 accept\n");
        if (dnsV6.Length > 0)
            builder.Append("    meta l4proto { tcp, udp } th dport 53 ip6 daddr @dns_v6 accept\n");
        builder.Append("    tcp dport 53 reject with tcp reset\n");
        builder.Append("    udp dport 53 drop\n");
        AppendPassRules(builder, passV4.Length > 0, passV6.Length > 0);
        AppendBlockRules(builder);
        builder.Append("  }\n");

        builder.Append("  chain forward {\n");
        builder.Append("    type filter hook forward priority filter; policy accept;\n");
        builder.Append($"    oifname \"{tunName}\" accept\n");
        builder.Append($"    iifname \"{tunName}\" accept\n");
        AppendPassRules(builder, passV4.Length > 0, passV6.Length > 0);
        AppendBlockRules(builder);
        builder.Append("  }\n");
        builder.Append("}\n");
        return builder.ToString();
    }

    private static void AppendSet(StringBuilder builder, string name, string type, IEnumerable<string> elements)
    {
        var list = elements.ToArray();
        if (list.Length == 0)
            return;

        builder.Append($"  set {name} {{\n");
        builder.Append($"    type {type}; flags interval; auto-merge;\n");
        builder.Append($"    elements = {{ {string.Join(", ", list)} }}\n");
        builder.Append("  }\n");
    }

    private static void AppendBlockRules(StringBuilder builder)
    {
        builder.Append("    meta l4proto tcp reject with tcp reset\n");
        builder.Append("    drop\n");
    }

    private static void AppendPassRules(StringBuilder builder, bool hasV4, bool hasV6)
    {
        if (hasV4)
            builder.Append("    ip daddr @pass_v4 accept\n");
        if (hasV6)
            builder.Append("    ip6 daddr @pass_v6 accept\n");
    }

    private static string FormatRange(IpRange ipRange)
    {
        return ipRange.FirstIpAddress.Equals(ipRange.LastIpAddress)
            ? ipRange.FirstIpAddress.ToString()
            : $"{ipRange.FirstIpAddress}-{ipRange.LastIpAddress}";
    }

    // The boot unit that loads the boot copy before the network comes up; written and enabled once, as its
    // content changes only with nft's path. Without systemd the block does not outlast a reboot.
    private static async Task EnsureBootUnit(string nftPath, CancellationToken cancellationToken)
    {
        if (!Directory.Exists("/run/systemd/system")) {
            VhLogger.Instance.LogWarning("No systemd here, so the kill switch's block does not outlast a reboot.");
            return;
        }

        var unit =
            "[Unit]\n" +
            "Description=VpnHood kill switch: its block, kept through a reboot\n" +
            "DefaultDependencies=no\n" +
            "After=local-fs.target\n" +
            "Before=network-pre.target\n" +
            "Wants=network-pre.target\n" +
            $"ConditionPathExists={BootFilePath}\n" +
            "\n" +
            "[Service]\n" +
            "Type=oneshot\n" +
            "RemainAfterExit=yes\n" +
            $"ExecStart={nftPath} -f {BootFilePath}\n" +
            "\n" +
            "[Install]\n" +
            "WantedBy=sysinit.target\n";

        if (File.Exists(BootUnitPath) && await File.ReadAllTextAsync(BootUnitPath, cancellationToken).Vhc() == unit)
            return;

        await FileUtils.WriteAllTextAtomicAsync(BootUnitPath, unit, cancellationToken).Vhc();
        await OsUtils.ExecuteCommandAsync("systemctl", "daemon-reload", cancellationToken).Vhc();
        await OsUtils.ExecuteCommandAsync("systemctl", $"enable {BootUnitName}", cancellationToken).Vhc();
    }

    private static string? FindNft()
    {
        return NftPaths.FirstOrDefault(File.Exists);
    }

    // This process's cgroup, by which the block lets the service out: cgroup v2's own line, "0::<path>".
    private static async Task<string> GetCgroupPath(CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync("/proc/self/cgroup", cancellationToken).Vhc();
        var line = lines.FirstOrDefault(x => x.StartsWith("0::", StringComparison.Ordinal));
        var path = line?[3..].Trim('/');
        if (string.IsNullOrEmpty(path))
            throw new NotSupportedException("The kill switch needs the service in a cgroup v2 group of its own.");

        return path;
    }
}
