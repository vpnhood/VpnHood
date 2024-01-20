namespace VpnHood.Client.App.Accounts
{
    public interface IAppUpdaterService
    {
        Task<bool> Update();
    }
}