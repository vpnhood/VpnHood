using System.Net.Sockets;
using VpnHood.Core.Toolkit.Exceptions;

namespace VpnHood.Core.Toolkit.ApiClients;

public static class ExceptionExtensions
{
    extension(Exception ex)
    {
        public ApiError ToApiError()
        {
            // make sure ToApiError of ApiException is called first
            if (ex is ApiException apiException)
                return apiException.ToApiError();

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
                InnerMessage = string.IsNullOrWhiteSpace(ex.Message) ? null : ex.InnerException?.Message
            };

            // add some exception data
            if (ex is SocketException socketException)
                apiError.Data["SocketErrorCode"] = socketException.SocketErrorCode.ToString();

            apiError.ImportData(ex.Data);
            return apiError;
        }

        private Type GetExceptionType()
        {
            if (AlreadyExistsException.Is(ex)) return typeof(AlreadyExistsException);
            if (NotExistsException.Is(ex)) return typeof(NotExistsException);

            return ex.GetType();
        }
    }
}