using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Common.Exceptions;

public class MaintenanceException(string? message = null)
    : SessionException(SessionErrorCode.Maintenance, message ?? "The server is in maintenance mode! Please try again later.");
