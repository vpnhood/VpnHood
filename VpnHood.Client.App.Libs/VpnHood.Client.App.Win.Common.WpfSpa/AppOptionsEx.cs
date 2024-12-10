namespace VpnHood.Client.App.Win.Common.WpfSpa;

public class AppOptionsEx(string appId) : AppOptions(appId)
{
    public bool ListenToAllIps { get; init; }
    public int? DefaultSpaPort { get; init; } = 80;
}