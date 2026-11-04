using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Net.Toolkit.Logging;

// A log level by its name, where a level this side does not know reads as Information.
public sealed class LogLevelJsonConverter() : JsonStringEnumFallbackConverter<LogLevel>(LogLevel.Information);
