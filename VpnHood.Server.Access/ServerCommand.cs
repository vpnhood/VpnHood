namespace VpnHood.Server.Access;

public class ServerCommand
{
    public string ConfigCode { get; set; }

    public ServerCommand(string configCode)
    {
        ConfigCode = configCode;
    }
}