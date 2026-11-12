using System.Reflection;
using System.Runtime.Serialization;
using VpnHood.Net.Toolkit.ApiClients;

namespace VpnHood.AppLib.Api.Exceptions;

// The name an ApiError carries, read back as the ExceptionType that stands for it, so a UI switches
// on the enum and the wire names live in one table. Null for a failure the contract has no name
// for, which a UI shows by its message.

/// <summary>Provides the reading of an API error as the <see cref="ExceptionType"/> it names.</summary>
public static class ApiErrorExtensions
{
    private static readonly Dictionary<string, ExceptionType> ByTypeName =
        Enum.GetValues<ExceptionType>().ToDictionary(WireName, x => x);

    /// <summary>Gets the error the API error names.</summary>
    /// <param name="apiError">The API error.</param>
    /// <returns>The error, or <see langword="null"/> for one the API has no name for, which the UI shows by its message.</returns>
    public static ExceptionType? GetExceptionType(this ApiError apiError)
    {
        return ByTypeName.TryGetValue(apiError.TypeName, out var exceptionType) ? exceptionType : null;
    }

    // the EnumMember value: the class name the ApiError carries and the OpenAPI document publishes
    private static string WireName(ExceptionType exceptionType)
    {
        var field = typeof(ExceptionType).GetField(exceptionType.ToString())
                    ?? throw new InvalidOperationException($"{exceptionType} is not a member of {nameof(ExceptionType)}.");
        return field.GetCustomAttribute<EnumMemberAttribute>()?.Value ?? exceptionType.ToString();
    }
}
