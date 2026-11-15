using System.Runtime.InteropServices;

namespace VpnHood.Test.Device;

// systemd-resolved's links as the Linux adapter's commands see them: the list of their DNS domains, a link's domains
// set by its index, and a revert; with the ways the real ones go wrong
public class TestResolvedLinks
{
    private const string ListCommand = "resolvectl -- domain";
    private const string SetCommandStart = "resolvectl -- domain ";
    private const string RevertCommandStart = "resolvectl -- revert ";
    private readonly List<TestResolvedLink> _links = [];

    public List<string> Commands { get; } = [];
    public int? FailingLinkIndex { get; set; } // a change of this link fails

    public TestResolvedLink Add(int index, string name, params string[] domains)
    {
        var link = new TestResolvedLink(index, name) { Domains = domains };
        _links.Add(link);
        return link;
    }

    public TestResolvedLink AddManaged(int index, string name, bool ignoresEmptyList, params string[] domains)
    {
        var link = new TestResolvedLink(index, name) {
            Domains = domains,
            NetworkdDomains = domains,
            IgnoresEmptyList = ignoresEmptyList
        };
        _links.Add(link);
        return link;
    }

    public void Remove(TestResolvedLink link)
    {
        _links.Remove(link);
    }

    public bool IsManagedByNetworkd(int linkIndex)
    {
        return _links.Single(x => x.Index == linkIndex).NetworkdDomains != null;
    }

    public string Execute(string command)
    {
        Commands.Add(command);
        if (command == ListCommand)
            return "Global:\n" + string.Concat(_links.Select(x =>
                $"Link {x.Index} ({x.Name}):{string.Concat(x.Domains.Select(y => $" {y}"))}\n"));

        if (command.StartsWith(SetCommandStart, StringComparison.Ordinal)) {
            var words = command[SetCommandStart.Length..].Split(' ');
            var domains = words.Skip(1).Select(x => x.Trim('\'')).Where(x => x.Length > 0).ToArray();
            var link = GetLinkToChange(words[0]);
            if (domains.Length > 0 || !link.IgnoresEmptyList)
                link.Domains = domains;

            return "";
        }

        if (command.StartsWith(RevertCommandStart, StringComparison.Ordinal)) {
            var link = GetLinkToChange(command[RevertCommandStart.Length..]);
            link.Domains = link.NetworkdDomains ?? [];
            return "";
        }

        throw new NotSupportedException($"Test: no such command: {command}");
    }

    private TestResolvedLink GetLinkToChange(string indexWord)
    {
        var index = int.Parse(indexWord);
        if (index == FailingLinkIndex)
            throw new ExternalException("Test: resolvectl failed.", 1);

        return _links.Single(x => x.Index == index);
    }
}
