using System.Net.Sockets;
using VpnHood.Core.Toolkit.Exceptions;

namespace VpnHood.Core.Toolkit.ApiClients;

public static class ExceptionExtensions
{
    public static ApiError ToApiError(this Exception ex)
    {
        var exceptionType = GetExceptionType(ex);

        // set best message
        var message = ex.Message;
        if (string.IsNullOrWhiteSpace(message)) message = ex.InnerException?.Message;
        if (string.IsNullOrWhiteSpace(message)) message = exceptionType.FullName;

        // common properties
        var apiError = new ApiError {
            TypeName = exceptionType.Name,
            TypeFullName = exceptionType.FullName,
            Message = message ?? "",
            InnerMessage = string.IsNullOrWhiteSpace(ex.Message) ? null : ex.InnerException?.Message,
        };

        // add some exception data
        if (ex is SocketException socketException)
            apiError.Data["SocketErrorCode"] = socketException.SocketErrorCode.ToString();

        apiError.ImportData(ex.Data);
        return apiError;
    }

    private static Type GetExceptionType(this Exception ex)
    {
        if (AlreadyExistsException.Is(ex)) return typeof(AlreadyExistsException);
        if (NotExistsException.Is(ex)) return typeof(NotExistsException);

        return ex.GetType();
    }

}