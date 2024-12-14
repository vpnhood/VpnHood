using VpnHood.Core.Common.Logging;

namespace VpnHood.Core.Tunneling.Exceptions;

public class UdpClientQuotaException(int maxUdpClient)
    : Exception($"Maximum UdpClient has been reached. MaxUdpClient: {maxUdpClient}"), ISelfLog
{
    public void Log()
    {
        // do nothing
    }
}