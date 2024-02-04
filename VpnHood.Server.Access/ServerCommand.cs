namespace VpnHood.Server.Access;

public class ServerCommand(string configCode)
{
    public string ConfigCode { get; set; } = configCode;
}