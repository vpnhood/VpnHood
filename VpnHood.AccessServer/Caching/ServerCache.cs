namespace VpnHood.AccessServer.Caching;

public class ServerCache
{
    public ServerCache(Models.Server server)
    {
        Server = server;
    }

    public Models.Server Server { get; }
}