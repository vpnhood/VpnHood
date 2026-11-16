using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Net.VpnAdapters.LinuxTun;

// Takes the DNS domains off every link but an adapter's own, for as long as the adapter's DNS servers are to be the
// only ones the host asks (VpnAdapterOptions.UseExclusiveDns), and gives them back. systemd-resolved sends a name
// under a link's own domain to that link's servers, by that link, whatever the routes and the adapter's ~. say, so the
// network that gave the link its domain saw those names, and answered them.
// The commands and the look at systemd-networkd come from outside, so the tests run it on a link list of their own.
// Not thread-safe: the adapter calls it under its lock.
internal sealed class LinuxLinkDomainsKeeper(
    string adapterName,
    string recordPath,
    Func<string, string> executeCommand,
    Func<int, bool> isManagedByNetworkd)
{
    // "--" before the words: a domain may begin with a dash, and resolvectl would take it for an option of its own
    // ('--help' printed its help, said nothing was wrong, and set no domain)
    private const string ListCommand = "resolvectl -- domain";

    /// <summary>
    /// Takes the domains off every other link that has any, recording them first.
    /// </summary>
    /// <exception cref="InvalidOperationException">A link still has its domains after it.</exception>
    public void Take()
    {
        var links = ListOtherLinks().Where(x => x.Domains.Count > 0)
            .Select(x => x with { IsManagedByNetworkd = isManagedByNetworkd(x.Index) })
            .ToArray();

        if (links.Length == 0)
            return;

        // Recorded first: a crash from here on leaves the links without their domains, and the record is what the
        // app's next start gives them back from
        Record(links);
        foreach (var link in links) {
            VhLogger.Instance.LogInformation(
                "Taking the DNS domains off {InterfaceName} while the VPN is up. Domains: {Domains}",
                link.Name, string.Join(' ', link.Domains.Select(x => VhLogger.FormatHostName(x))));

            try {
                executeCommand(SetCommand(link.Index, []));
            }
            catch (Exception ex) {
                throw CreateKeptException(link.Name, ex);
            }
        }

        // Looked at again, not taken on the command's word. For a link that systemd-networkd manages, resolvectl of
        // systemd 245 and 249 hands the empty list on to networkd, to which it means the configured domains: the
        // command says nothing is wrong and the domain stays (seen on both). From 255 on the domain goes.
        var kept = ListOtherLinks().FirstOrDefault(x => x.Domains.Count > 0);
        if (kept != null)
            throw CreateKeptException(kept.Name, innerException: null);
    }

    /// <summary>
    /// Gives each recorded link its domains back, and drops from the record what is settled.
    /// </summary>
    /// <remarks>
    /// A link is settled when it has its domains again, from this call or from its own manager since, or is gone. One
    /// whose command fails stays in the record for the next call, and the failure is logged.
    /// </remarks>
    public void GiveBack()
    {
        if (!File.Exists(recordPath))
            return;

        var recorded = ReadRecordOrDrop();
        if (recorded.Count == 0)
            return;

        var links = LinuxLinkDomains.ParseAll(executeCommand(ListCommand));
        var unsettled = new List<LinuxLinkDomains>();
        foreach (var link in recorded) {
            // By its index, which no other link gets while the system is up: its name may have changed since. A
            // link that is gone has none to get, and one with domains again got them from its own manager.
            if (!links.Any(x => x.Index == link.Index && x.Domains.Count == 0))
                continue;

            try {
                VhLogger.Instance.LogInformation("Giving {InterfaceName} its DNS domains back.", link.Name);
                executeCommand(GiveBackCommand(link));
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex,
                    "Could not give {InterfaceName} its DNS domains back; the next start tries again.", link.Name);
                unsettled.Add(link);
            }
        }

        if (unsettled.Count > 0)
            WriteRecord(unsettled);
        else
            File.Delete(recordPath);
    }

    // A link that systemd-networkd managed when its domains were taken goes back to networkd by a revert: given its
    // domains by name, it would keep them through networkd's later changes, as a setting made at run time stands
    // until a revert (seen on systemd 255, 257 and 259). The revert hands networkd the link's other DNS settings
    // too, so one made at run time by someone else goes with it; nothing tells such a setting from networkd's own.
    // Never a revert for another link: all its settings are made at run time, by NetworkManager as by hand, and its
    // servers would go with its domains.
    private static string GiveBackCommand(LinuxLinkDomains link)
    {
        return link.IsManagedByNetworkd
            ? $"resolvectl -- revert {link.Index}"
            : SetCommand(link.Index, link.Domains);
    }

    // By its index, which needs no quoting; each domain between single quotes, which nothing in a domain ends
    // (LinuxLinkDomains). No domain at all is the empty word, which takes them off.
    internal static string SetCommand(int linkIndex, IReadOnlyList<string> domains)
    {
        var words = domains.Count > 0 ? string.Join(' ', domains.Select(x => $"'{x}'")) : "''";
        return $"resolvectl -- domain {linkIndex} {words}";
    }

    private LinuxLinkDomains[] ListOtherLinks()
    {
        return LinuxLinkDomains.ParseAll(executeCommand(ListCommand))
            .Where(x => x.Name != adapterName)
            .ToArray();
    }

    private static InvalidOperationException CreateKeptException(string linkName, Exception? innerException)
    {
        var message =
            $"Could not keep DNS inside the VPN: the interface {linkName} has a DNS domain of its own, whose names " +
            "are looked up by that interface, outside the VPN, and the system does not let the VPN take it off. " +
            "To connect on this network, choose Allow Bypass VPN in the app's Split DNS settings.";

        return innerException != null
            ? new InvalidOperationException($"{message}\nresolvectl error: {innerException.Message}", innerException)
            : new InvalidOperationException(message);
    }

    // A link keeps its last entry: one taken before, which has no domain now, is not among the new ones. A record
    // that is there and cannot be read fails the taking, before any link is touched: written over, what it holds of
    // the links taken before would be lost.
    private void Record(IReadOnlyList<LinuxLinkDomains> links)
    {
        var recorded = File.Exists(recordPath) ? LinuxLinkDomains.ParseAll(File.ReadAllText(recordPath)) : [];
        WriteRecord(recorded.Where(x => links.All(y => y.Index != x.Index)).Concat(links));
    }

    private void WriteRecord(IEnumerable<LinuxLinkDomains> links)
    {
        if (Path.GetDirectoryName(recordPath) is { Length: > 0 } folderPath)
            Directory.CreateDirectory(folderPath);

        FileUtils.WriteAllTextAtomic(recordPath, string.Join('\n', links.Select(x => x.ToLine())) + "\n");
    }

    // A record whose lines cannot be read gives nothing back, now or later: it goes, and the loss is said
    private IReadOnlyList<LinuxLinkDomains> ReadRecordOrDrop()
    {
        var text = File.ReadAllText(recordPath);
        try {
            return LinuxLinkDomains.ParseAll(text);
        }
        catch (FormatException ex) {
            VhLogger.Instance.LogError(ex,
                "The record of the DNS domains taken off the interfaces cannot be read, so they are not given back. " +
                "A reconnect of the network sets them again.");
            File.Delete(recordPath);
            return [];
        }
    }
}
