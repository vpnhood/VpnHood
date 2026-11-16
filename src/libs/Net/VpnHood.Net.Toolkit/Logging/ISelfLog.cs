namespace VpnHood.Net.Toolkit.Logging;

/// <summary>
/// Represents an object that writes its own state to the log.
/// </summary>
public interface ISelfLog
{
    /// <summary>Writes the object's state to the log.</summary>
    public void Log();
}