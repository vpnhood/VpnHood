using System;
using VpnHood.Common.Logging;

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