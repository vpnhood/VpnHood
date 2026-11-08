namespace VpnHood.Net.Toolkit.Logging;

/// <summary>
/// Represents a log scope's data: name and value pairs.
/// </summary>
public class LogScope
{
    /// <summary>Gets the scope's name and value pairs.</summary>
    public List<Tuple<string, object?>> Data { get; } = [];
}