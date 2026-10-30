namespace VpnHood.Core.Server.Listeners.Http3;

// Internal inspection limit, not a malformed HTTP message. The application can send its final response.
internal sealed class Http3ReadBudgetExceededException : Exception;
