using System;
using VpnHood.Server.Exceptions;

namespace VpnHood.Tunneling.Exceptions;

public class UdpClientQuotaException : Exception, ISelfLog
{
    public UdpClientQuotaException(int maxUdpClient) 
        : base($"Maximum UdpClient has been reached. MaxUdpClient: {maxUdpClient}")
    {

    }

    public void Log()
    {
        // do nothing
    }
}