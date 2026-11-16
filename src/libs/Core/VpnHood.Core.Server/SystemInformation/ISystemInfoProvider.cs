namespace VpnHood.Core.Server.SystemInformation;

/// <summary>Represents what tells the server of its machine: its OS, memory and processors.</summary>
public interface ISystemInfoProvider
{
    /// <summary>Gets the machine's information now.</summary>
    /// <returns>The information.</returns>
    SystemInfo GetSystemInfo();
}