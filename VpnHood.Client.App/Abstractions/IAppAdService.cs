namespace VpnHood.Client.App.Abstractions;

public interface IAppAdService : IDisposable
{
    Task ShowAd(string customData, CancellationToken cancellationToken);
}