using VpnHood.Common.Logging;

namespace VpnHood.Tunneling.Exceptions;

public class UdpClientQuotaException(int maxUdpClient)
    : Exception($"Maximum UdpClient has been reached. MaxUdpClient: {maxUdpClient}"), ISelfLog
{
    public void Log()
    {
        // do nothing
    }
}