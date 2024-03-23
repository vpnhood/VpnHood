namespace VpnHood.Client.App.Abstractions;

public interface IAppAdService : IDisposable
{
    Task<string> ShowAd(CancellationToken cancellationToken);
}