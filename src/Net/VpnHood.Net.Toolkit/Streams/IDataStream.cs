namespace VpnHood.Net.Toolkit.Streams;

/// <summary>
/// Represents a stream that can tell whether data is waiting to be read.
/// </summary>
public interface IDataStream
{
    /// <summary>
    /// Gets a value indicating whether data is waiting to be read, or <see langword="null"/> when the stream cannot tell.
    /// </summary>
    bool? DataAvailable { get; }
}