using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using VpnHood.Common.Exceptions;

namespace VpnHood.Common.Client;

public class ApiError
{
    public string TypeName { get; set; }
    public string? TypeFullName { get; set; }
    public string Message { get; set; }
    public Dictionary<string, string?> Data { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? InnerMessage { get; set; }

    [JsonConstructor]
    public ApiError(string typeName, string message)
    {
        TypeName = typeName;
        Message = message;
    }

    public ApiError(Exception ex)
    {
        var exceptionType = GetExceptionType(ex);

        TypeName = exceptionType.Name;
        TypeFullName = exceptionType.FullName;
        Message = ex.Message;
        InnerMessage = ex.InnerException?.Message;

        foreach (DictionaryEntry item in ex.Data)
        {
            var key = item.Key.ToString();
            if (key != null)
                Data.Add(key, item.Value?.ToString());
        }
    }

    private static Type GetExceptionType(Exception ex)
    {
        if (AlreadyExistsException.Is(ex)) return typeof(AlreadyExistsException);
        if (NotExistsException.Is(ex)) return typeof(NotExistsException);
        return ex.GetType();
    }

    public static bool TryParse(string value, [NotNullWhen(true)] out ApiError? apiError)
    {
        apiError = null;

        try
        {
            var res = JsonSerializer.Deserialize<ApiError>(value);
            if (res?.TypeName == null)
                return false;

            apiError = res;
            return true;
        }
        catch
        {
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
}