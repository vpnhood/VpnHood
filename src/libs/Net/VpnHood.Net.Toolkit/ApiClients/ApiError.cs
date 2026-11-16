using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using VpnHood.Net.Toolkit.Exceptions;

namespace VpnHood.Net.Toolkit.ApiClients;

/// <summary>
/// Represents an exception as an API sends it: its type's names, its message, its data and its inner exception's
/// message, as JSON a client turns back into an exception.
/// </summary>
public class ApiError : ICloneable, IEquatable<ApiError>
{
    /// <summary>The data key that marks an exception made from an <see cref="ApiError"/>: <c>IsApiError</c>.</summary>
    public const string Flag = "IsApiError";

    // A copy of the context's settings, as new ones would write by reflection, which the browser page has off.
    private static readonly JsonSerializerOptions IndentedOptions = new(ApiErrorJsonContext.Default.Options) { WriteIndented = true };

    /// <summary>Gets the exception type's name, such as <c>NotExistsException</c>.</summary>
    public required string TypeName { get; init; }

    /// <summary>Gets the exception type's full name, or <see langword="null"/>.</summary>
    public string? TypeFullName { get; init; }

    /// <summary>Gets the exception's message.</summary>
    public required string Message { get; init; }

    /// <summary>Gets the exception's data, as text. The default is none, and JSON null reads as none.</summary>
    [AllowNull]
    public Dictionary<string, string?> Data {
        get;
        init => field = value ?? new Dictionary<string, string?>();
    } = new();

    /// <summary>Gets the inner exception's message, or <see langword="null"/>.</summary>
    public string? InnerMessage { get; init; }

    /// <summary>Copies the error, with a data dictionary of its own.</summary>
    /// <returns>The copy.</returns>
    public object Clone() => new ApiError {
        TypeName = TypeName,
        TypeFullName = TypeFullName,
        Message = Message,
        Data = new Dictionary<string, string?>(Data),
        InnerMessage = InnerMessage
    };


    /// <summary>Tries to read an error from its JSON, whose names match ignoring case.</summary>
    /// <param name="value">The JSON.</param>
    /// <param name="apiError">The error, when read; otherwise, <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="true"/> if the JSON is an error with a type name and a message; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool TryParse(string value, [NotNullWhen(true)] out ApiError? apiError)
    {
        apiError = null;

        try {
            var res = JsonSerializer.Deserialize(value, ApiErrorJsonContext.Default.ApiError);
            if (res?.TypeName == null)
                return false;

            apiError = res;
            return true;
        }
        catch {
            return false;
        }
    }

    /// <summary>Reads an error from its JSON, whose names match ignoring case.</summary>
    /// <param name="value">The JSON.</param>
    /// <returns>The error.</returns>
    /// <exception cref="FormatException">The JSON is not an error with a type name and a message.</exception>
    public static ApiError Parse(string value)
    {
        if (TryParse(value, out var apiError))
            return apiError;

        throw new FormatException("Invalid ApiError format.");
    }

    /// <summary>Writes the error as JSON.</summary>
    /// <param name="writeIndented"><see langword="true"/> to indent the JSON.</param>
    /// <returns>The JSON.</returns>
    public string ToJson(bool writeIndented = false)
    {
        var options = writeIndented ? IndentedOptions : ApiErrorJsonContext.Default.Options;
        return JsonSerializer.Serialize(this, options.GetTypeInfo(typeof(ApiError)));
    }

    /// <summary>Determines whether the error's type has the name of a type.</summary>
    /// <typeparam name="T">The type.</typeparam>
    /// <returns><see langword="true"/> if the names match; otherwise, <see langword="false"/>.</returns>
    public bool Is<T>()
    {
        return TypeName == typeof(T).Name;
    }

    /// <summary>
    /// Makes the exception the error stands for: one of the common types it names, such as
    /// <see cref="OperationCanceledException"/>, <see cref="TimeoutException"/> or <see cref="NotExistsException"/>,
    /// or else an <see cref="ApiException"/>, with the error's data and names in its <see cref="Exception.Data"/>.
    /// </summary>
    /// <returns>The exception.</returns>
    public Exception ToException()
    {
        // create exception
        var innerException = new Exception(InnerMessage ?? "");
        Exception exception =
            Is<OperationCanceledException>() ? new OperationCanceledException(Message, innerException) :
            Is<TaskCanceledException>() ? new TaskCanceledException(Message, innerException) :
            Is<AlreadyExistsException>() ? AlreadyExistsException.FromMessage(Message, innerException) :
            Is<NotExistsException>() ? new NotExistsException(Message, innerException) :
            Is<UnauthorizedAccessException>() ? new UnauthorizedAccessException(Message, innerException) :
            Is<TimeoutException>() ? new TimeoutException(Message, innerException) :
            Is<InvalidOperationException>() ? new InvalidOperationException(Message, innerException) :
            new ApiException(this);

        ExportData(exception.Data);
        return exception;
    }

    /// <summary>Adds an exception's data to the error's, as text, keeping the keys it has, and adds <see cref="Flag"/>.</summary>
    /// <param name="data">The exception's data.</param>
    public void ImportData(IDictionary data)
    {
        foreach (DictionaryEntry item in data) {
            var key = item.Key.ToString();
            if (key != null)
                Data.TryAdd(key, item.Value?.ToString());
        }

        Data.TryAdd(Flag, "true");
    }

    /// <summary>
    /// Adds the error's data, its type's names and <see cref="Flag"/> to an exception's data, keeping the keys it has.
    /// </summary>
    /// <param name="data">The exception's data.</param>
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

    /// <summary>Determines whether another error has the same names, messages and data.</summary>
    /// <param name="other">The other error.</param>
    /// <returns><see langword="true"/> if they are the same; otherwise, <see langword="false"/>.</returns>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return HashCode.Combine(TypeName, TypeFullName, Message, InnerMessage);
    }

    /// <summary>Returns the error's names, messages and data on one line.</summary>
    /// <returns>The text.</returns>
    public override string ToString()
    {
        return $"ApiError: TypeName={TypeName}, " +
               $"TypeFullName={TypeFullName}, " +
               $"Message={Message}, " +
               $"InnerMessage={InnerMessage}, " +
               $"Data=[{string.Join(", ", Data.Select(kvp => $"{kvp.Key}={kvp.Value}"))}]";
    }
}