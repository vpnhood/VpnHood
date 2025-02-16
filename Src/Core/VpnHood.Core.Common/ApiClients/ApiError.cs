using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using VpnHood.Core.Common.Exceptions;

namespace VpnHood.Core.Common.ApiClients;

public class ApiError : ICloneable
{
    public const string Flag = "IsApiError";
    public required string TypeName { get; init; }
    public string? TypeFullName { get; set; }
    public required string Message { get; init; }
    public Dictionary<string, string?> Data { get; set; } = new();
    public string? InnerMessage { get; set; }

    public object Clone() => new ApiError {
        TypeName = TypeName,
        TypeFullName = TypeFullName,
        Message = Message,
        Data = new Dictionary<string, string?>(Data),
        InnerMessage = InnerMessage
    };


    public static bool TryParse(string value, [NotNullWhen(true)] out ApiError? apiError)
    {
        apiError = null;

        try {
            var res = JsonSerializer.Deserialize<ApiError>(value);
            if (res?.TypeName == null)
                return false;

            apiError = res;
            return true;
        }
        catch {
            return false;
        }
    }

    public static ApiError Parse(string value)
    {
        if (TryParse(value, out var apiError))
            return apiError;

        throw new FormatException("Invalid ApiError format.");
    }

    public string ToJson(bool writeIndented = false)
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = writeIndented });
    }

    public bool Is<T>()
    {
        return TypeName == typeof(T).Name;
    }

    public Exception ToException()
    {
        // create exception
        var innerException = new Exception(InnerMessage ?? "");
        Exception? exception;

        if (Is<OperationCanceledException>())
            exception = new OperationCanceledException(Message, innerException);

        else if (Is<TaskCanceledException>())
            exception = new TaskCanceledException(Message, innerException);

        else if (Is<AlreadyExistsException>())
            exception = new AlreadyExistsException(Message, innerException);

        else if (Is<NotExistsException>())
            exception = new NotExistsException(Message, innerException);

        else if (Is<UnauthorizedAccessException>())
            exception = new UnauthorizedAccessException(Message, innerException);

        else if (Is<TimeoutException>())
            exception = new TimeoutException(Message, innerException);

        else if (Is<InvalidOperationException>())
            exception = new InvalidOperationException(Message, innerException);
        else
            exception = new ApiException(this);

        ExportData(exception.Data);
        return exception;
    }

    public void ImportData(IDictionary data)
    {
        foreach (DictionaryEntry item in data) {
            var key = item.Key.ToString();
            if (key != null)
                Data.TryAdd(key, item.Value?.ToString());
        }

        if (data.Contains(Flag))
            data.Add(Flag, "true");
    }

    public void ExportData(IDictionary data)
    {
        foreach (var kvp in Data.Where(kvp => !data.Contains(kvp.Key)))
            data.Add(kvp.Key, kvp.Value);

        Data.TryAdd(nameof(TypeFullName), TypeFullName);
        Data.TryAdd(nameof(TypeName), TypeName);
        Data.TryAdd("IsApiError", "true");

    }
}