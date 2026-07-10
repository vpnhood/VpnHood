namespace VpnHood.Core.Filtering.Abstractions;

// Descriptor for one SQLite split-ip db pipe stage: where the db lives and what membership means.
// Include ⇒ tunnel only members; Exclude ⇒ bypass members; Block ⇒ drop members.
// Chained Include gates compose as set intersection. See docs/split-ip/README.md.
public class SplitIpDbFilter
{
    public required string DbPath { get; init; }
    public required FilterAction Action { get; init; }
}
