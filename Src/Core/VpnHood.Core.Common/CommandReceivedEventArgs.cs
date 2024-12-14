namespace VpnHood.Core.Common;

public class CommandReceivedEventArgs(string[] arguments) : EventArgs
{
    public string[] Arguments = arguments;
}