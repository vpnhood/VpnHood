namespace VpnHood.Net.Toolkit.Streams;

public interface IDataStream
{
    bool? DataAvailable { get; }
}