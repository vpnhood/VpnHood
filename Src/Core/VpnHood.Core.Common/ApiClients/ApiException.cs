
namespace VpnHood.Core.Common.ApiClients;

public sealed class ApiException : Exception
{
    public int StatusCode { get; }
    public string? Response { get; }
    public string? ExceptionTypeName { get; }
    public string? ExceptionTypeFullName { get; }
    public IReadOnlyDictionary<string, IEnumerable<string>> Headers { get; }

    private static string BuildMessage(string message, int statusCode, string? response)
    {
        return response != null && ApiError.TryParse(response, out var apiError)
            ? apiError.Message
            : $"{message}\n\nStatus: {statusCode}\nResponse: \n{response?[..Math.Min(512, response.Length)]}";
    }

    public ApiException(string message, int statusCode, string? response,
        IReadOnlyDictionary<string, IEnumerable<string>>? headers, Exception? innerException)
        : base(BuildMessage(message, statusCode, response), innerException)
    {
        StatusCode = statusCode;
        Response = response;
        Headers = headers ?? new Dictionary<string, IEnumerable<string>>();

        //try to deserialize response
        if (response != null && ApiError.TryParse(response, out var apiError)) {
            foreach (var item in apiError.Data)
                Data.Add(item.Key, item.Value);

            ExceptionTypeName = apiError.TypeName;
            ExceptionTypeFullName = apiError.TypeFullName;
        }
    }

    public ApiException(ApiError apiError)
        : base(apiError.Message, new Exception(apiError.InnerMessage ?? ""))
    {
        StatusCode = 400;
        ExceptionTypeFullName = apiError.TypeFullName;
        ExceptionTypeName = apiError.TypeName;
        Headers = new Dictionary<string, IEnumerable<string>>();
        apiError.ExportData(Data);
    }

    public ApiError ToApiError()
    {
        var apiError = new ApiError {
            TypeName = ExceptionTypeName ?? GetType().Name,
            TypeFullName = ExceptionTypeFullName ?? GetType().FullName,
            Message = Message,
            InnerMessage = InnerException?.Message
        };
        apiError.ImportData(Data);
        apiError.Data.TryAdd("InnerStatusCode", StatusCode.ToString());

        return apiError;
    }

    public override string ToString()
    {
        return $"HTTP Response: \n\n{Response}\n\n{base.ToString()}";
    }

    public bool Is<T>()
    {
        return ExceptionTypeName == typeof(T).Name;
    }
}