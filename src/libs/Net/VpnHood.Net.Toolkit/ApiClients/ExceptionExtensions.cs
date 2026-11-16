using System.Net.Sockets;
using VpnHood.Net.Toolkit.Exceptions;

namespace VpnHood.Net.Toolkit.ApiClients;

/// <summary>
/// Provides the means to send an exception from an API as an <see cref="ApiError"/>.
/// </summary>
public static class ExceptionExtensions
{
    /// <summary>Provides an exception's <see cref="ApiError"/>.</summary>
    /// <param name="ex">The exception.</param>
    extension(Exception ex)
    {
        /// <summary>
        /// Makes the exception's error: an <see cref="ApiException"/>'s own, or one with the exception's type, message,
        /// data and inner message. By a heuristic, a database's duplicate or missing row counts as
        /// <see cref="AlreadyExistsException"/> or <see cref="NotExistsException"/>; a socket error adds its code.
        /// </summary>
        /// <returns>The error.</returns>
        public ApiError ToApiError()
        {
            // make sure ToApiError of ApiException is called first
            if (ex is ApiException apiException)
                return apiException.ToApiError();

            var exceptionType = ex.GetExceptionType();

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