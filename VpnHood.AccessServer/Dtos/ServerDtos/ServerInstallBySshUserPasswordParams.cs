namespace VpnHood.AccessServer.Dtos.ServerDtos;

public class ServerInstallBySshUserPasswordParams
{
    public string HostName { get; set; }
    public int HostPort { get; set; } = 22;
    public string UserName { get; set; }
    public string Password { get; set; }

    public ServerInstallBySshUserPasswordParams(string hostName, string userName, string password)
    {
        HostName = hostName;
        UserName = userName;
        Password = password;
    }
}