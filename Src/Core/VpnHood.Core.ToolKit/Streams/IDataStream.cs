namespace VpnHood.Core.Toolkit.Streams;

public interface IDataStream
{
    bool? DataAvailable { get; }
}