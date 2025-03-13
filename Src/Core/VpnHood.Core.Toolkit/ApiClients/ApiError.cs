using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using VpnHood.Core.Toolkit.Exceptions;

namespace VpnHood.Core.Toolkit.ApiClients;

public class ApiError : ICloneable, IEquatable<ApiError>
{
    public const string Flag = "IsApiError";
    public required string TypeName { get; init; }
    public string? TypeFullName { get; init; }
    public required string Message { get; init; }
    public Dictionary<string, string?> Data { get; init; } = new();
    public string? InnerMessage { get; init; }

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
        Exception exception =
            Is<OperationCanceledException>() ? new OperationCanceledException(Message, innerException) :
            Is<TaskCanceledException>() ? new TaskCanceledException(Message, innerException) :
            Is<AlreadyExistsException>() ? new AlreadyExistsException(Message, innerException) :
            Is<NotExistsException>() ? new NotExistsException(Message, innerException) :
            Is<UnauthorizedAccessException>() ? new UnauthorizedAccessException(Message, innerException) :
            Is<TimeoutException>() ? new TimeoutException(Message, innerException) :
            Is<InvalidOperationException>() ? new InvalidOperationException(Message, innerException) :
            new ApiException(this);

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

        Data.TryAdd(Flag, "true");
    }

    public void ExportData(IDictionary data)
    {
        foreach (var kvp in Data.Where(kvp => !data.Contains(kvp.Key)))
            data.Add(kvp.Key, kvp.Value);

        if (!data.Contains(nameof(TypeFullName)))
            data.Add(nameof(TypeFullName), TypeFullName);

        if (!data.Contains(nameof(TypeName)))
            data.Add(nameof(TypeName), TypeName);

        if (!data.Contains(Flag))
            data.Add(Flag, "true");
    }

    public bool Equals(ApiError? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return TypeName == other.TypeName &&
               TypeFullName == other.TypeFullName &&
               Message == other.Message &&
               InnerMessage == other.InnerMessage &&
               DictionariesEqual(Data, other.Data);
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((ApiError)obj);
    }

    private static bool DictionariesEqual(Dictionary<string, string?> dict1, Dictionary<string, string?> dict2)
    {
        if (dict1.Count != dict2.Count)
            return false;

        foreach (var kvp in dict1) {
            if (!dict2.TryGetValue(kvp.Key, out var value) || !string.Equals(kvp.Value, value))
                return false;
        }

        return true;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(TypeName, TypeFullName, Message, InnerMessage);
    }

}