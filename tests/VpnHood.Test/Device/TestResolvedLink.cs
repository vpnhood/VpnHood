namespace VpnHood.Test.Device;

// A link as systemd-resolved has it, for TestResolvedLinks
public class TestResolvedLink(int index, string name)
{
    public int Index { get; } = index;
    public string Name { get; set; } = name;
    public string[] Domains { get; set; } = [];

    // what systemd-networkd has configured for a link it manages, which a revert brings back; null for a link of
    // another manager's, whose settings are all made at run time
    public string[]? NetworkdDomains { get; set; }

    // as systemd 245 and 249 for a link systemd-networkd manages: the empty list is taken, and the domains stay
    public bool IgnoresEmptyList { get; init; }
}
