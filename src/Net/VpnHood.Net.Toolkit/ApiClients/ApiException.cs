namespace VpnHood.Net.Toolkit.ApiClients;

/// <summary>
/// The exception that is thrown when an API call fails: its status code, its response and, when the response is an
/// <see cref="ApiError"/>, the type of the exception the API threw.
/// </summary>
public sealed class ApiException : Exception
{
    /// <summary>Gets the HTTP status code; 400 for one made from an <see cref="ApiError"/>.</summary>
    public int StatusCode { get; }

    /// <summary>Gets the response's body, or <see langword="null"/>.</summary>
    public string? Response { get; }

    /// <summary>Gets the name of the exception type the API threw, or <see langword="null"/> when the response is no error.</summary>
    public string? ExceptionTypeName { get; }

    /// <summary>Gets the full name of the exception type the API threw, or <see langword="null"/>.</summary>
    public string? ExceptionTypeFullName { get; }

    /// <summary>Gets the response's headers.</summary>
    public IReadOnlyDictionary<string, IEnumerable<string>> Headers { get; }

    private static string BuildMessage(string message, int statusCode, string? response)
    {
        return response != null && ApiError.TryParse(response, out var apiError)
            ? apiError.Message
            : $"{message}\n\nStatus: {statusCode}\nResponse: \n{response?[..Math.Min(512, response.Length)]}";
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiException"/> class from a response. When the response is an
    /// <see cref="ApiError"/>, its message, type and data are the exception's; otherwise, the message carries the status
    /// and the response's first 512 characters.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="response">The response's body, or <see langword="null"/>.</param>
    /// <param name="headers">The response's headers, or <see langword="null"/> for none.</param>
    /// <param name="innerException">The exception that is the cause of this one, or <see langword="null"/>.</param>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiException"/> class from an error, with status code 400.
    /// </summary>
    /// <param name="apiError">The error.</param>
    public ApiException(ApiError apiError)
        : base(apiError.Message, new Exception(apiError.InnerMessage ?? ""))
    {
        StatusCode = 400;
        ExceptionTypeFullName = apiError.TypeFullName;
        ExceptionTypeName = apiError.TypeName;
        Headers = new Dictionary<string, IEnumerable<string>>();
        apiError.ExportData(Data);
    }

    /// <summary>
    /// Makes the error the exception stands for, with the type the API threw, and the status code in its data as
    /// <c>InnerStatusCode</c>.
    /// </summary>
    /// <returns>The error.</returns>
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

    /// <summary>Returns the response's body, then the exception's own text.</summary>
    /// <returns>The text.</returns>
    public override string ToString()
    {
        return $"HTTP Response: \n\n{Response}\n\n{base.ToString()}";
    }

    /// <summary>Determines whether the API threw an exception with the name of a type.</summary>
    /// <typeparam name="T">The type.</typeparam>
    /// <returns><see langword="true"/> if the names match; otherwise, <see langword="false"/>.</returns>
    public bool Is<T>()
    {
        return ExceptionTypeName == typeof(T).Name;
    }
}