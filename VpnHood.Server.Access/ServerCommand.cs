using System;

namespace VpnHood.Server;

public class ServerCommand
{
    public string ConfigCode { get; set; }

    public ServerCommand(string configCode)
    {
        ConfigCode = configCode;
    }
}