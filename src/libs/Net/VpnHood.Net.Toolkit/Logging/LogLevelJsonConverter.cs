using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Net.Toolkit.Logging;

/// <summary>
/// Converts a log level by its name; a level this side does not know reads as Information.
/// </summary>
public sealed class LogLevelJsonConverter() : JsonStringEnumFallbackConverter<LogLevel>(LogLevel.Information);
