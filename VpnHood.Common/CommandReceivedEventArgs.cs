using System;

namespace VpnHood.Common;

public class CommandReceivedEventArgs : EventArgs
{
    public string[] Arguments;
    public CommandReceivedEventArgs(string[] arguments)
    {
        Arguments = arguments;
    }
}