namespace VpnHood.AppLib.Contracts.Settings;

// Where a proxy list is fetched from and how it is pruned. Saved with the rest of the settings, so
// the names here are the settings file's names as much as the wire's.
public class ProxyAutoUpdateOptions
{
    public Uri? Url { get; set; }
    public TimeSpan? Interval { get; set; }
    public int? MaxPenalty { get; set; }
    public int? MaxItemCount { get; set; }
    public bool RemoveDuplicateIps { get; set; } = true;
}
