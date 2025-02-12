using System.Net.Sockets;
using VpnHood.Core.Common.Exceptions;

namespace VpnHood.Core.Common.ApiClients;

public static class ExceptionExtensions
{
    public static ApiError ToApiError(this Exception ex)
    {
        var exceptionType = GetExceptionType(ex);

        // common properties
        var apiError = new ApiError {
            TypeName = exceptionType.Name,
            TypeFullName = exceptionType.FullName,
            Message = ex.Message,
            InnerMessage = ex.InnerException?.Message,
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