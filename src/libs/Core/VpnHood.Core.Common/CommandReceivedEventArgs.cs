namespace VpnHood.Core.Common;

/// <summary>
/// Provides the arguments of a command a <see cref="CommandListener"/> received.
/// </summary>
/// <param name="arguments">The command's arguments.</param>
public class CommandReceivedEventArgs(string[] arguments) : EventArgs
{
    /// <summary>Gets the command's arguments.</summary>
    public string[] Arguments => arguments;
}