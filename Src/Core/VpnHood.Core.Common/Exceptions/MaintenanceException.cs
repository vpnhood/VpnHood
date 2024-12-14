using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Common.Exceptions;

public class MaintenanceException : SessionException
{
    public MaintenanceException()
        : base(SessionErrorCode.Maintenance, "The server is in maintenance mode! Please try again later.")
    {
    }

    public MaintenanceException(string message)
        : base(SessionErrorCode.Maintenance, message)
    {
    }
}