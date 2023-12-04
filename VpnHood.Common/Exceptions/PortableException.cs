using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpnHood.Common.Exceptions;

public class PortableException
{
    public string TypeName { get; set; }
    public string? TypeFullName { get; set; }
    public string Message { get; set; }
    public string? InnerMessage { get; set; }
    public Dictionary<string, string?> Data { get; set; } = new();

    [JsonConstructor]
    public PortableException(string typeName, string message)
    {
        TypeName = typeName;
        Message = message;
    }

    public PortableException(Exception ex)
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

    public static bool TryParse(string value, [NotNullWhen(true)] out PortableException? portableException)
    {
        portableException = null;

        try
        {
            var res = JsonSerializer.Deserialize<PortableException>(value);
            if (res?.TypeName == null)
                return false;

            portableException = res;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static PortableException Parse(string value)
    {
        if (TryParse(value, out var portableException))
            return portableException;

        throw new FormatException("Invalid PortableException format.");
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