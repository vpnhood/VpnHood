using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using VpnHood.Core.Common.Exceptions;

namespace VpnHood.Core.Common.ApiClients;

public class ApiError : ICloneable
{
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
        var exception = ToException(innerException);

        // add data
        foreach (var item in Data)
            exception.Data.Add(item.Key, item.Value);

        // add type info
        if (!exception.Data.Contains(nameof(TypeFullName)))
            exception.Data.Add(nameof(TypeFullName), TypeFullName);

        // add type info
        if (!exception.Data.Contains(nameof(TypeName)))
            exception.Data.Add(nameof(TypeName), TypeName);

        return exception;
    }

    private Exception ToException(Exception innerException)
    {
        if (Is<OperationCanceledException>())
            return new OperationCanceledException(Message, innerException);

        if (Is<TaskCanceledException>())
            return new TaskCanceledException(Message, innerException);

        if (Is<AlreadyExistsException>())
            return new AlreadyExistsException(Message, innerException);

        if (Is<NotExistsException>())
            return new NotExistsException(Message, innerException);

        if (Is<UnauthorizedAccessException>())
            return new UnauthorizedAccessException(Message, innerException);

        return new Exception(Message, innerException);
    }

    public void ImportData(IDictionary data)
    {
        foreach (DictionaryEntry item in data) {
            var key = item.Key.ToString();
            if (key != null)
                Data.TryAdd(key, item.Value?.ToString());
        }
    }

    public void ExportData(IDictionary data)
    {
        foreach (var kvp in Data.Where(kvp => !data.Contains(kvp.Key)))
            data.Add(kvp.Key, kvp.Value);
    }

}