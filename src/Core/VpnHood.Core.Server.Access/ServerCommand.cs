using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Server.Access;

/// <summary>
/// Represents the access manager's answer to a server's status.
/// </summary>
/// <param name="configCode">The code of the configuration the access manager wants the server on.</param>
public class ServerCommand(string configCode)
{
    /// <summary>
    /// Gets or sets the code of the configuration the access manager wants the server on: where it differs from the
    /// server's, the server asks for its configuration again.
    /// </summary>
    public string ConfigCode { get; set; } = configCode;

    /// <summary>
    /// Gets or sets the states of sessions, by their IDs: those whose usage came with the status, though one may be
    /// left out, and others the access manager has news of. The default is none.
    /// </summary>
    public Dictionary<ulong, SessionResponse> SessionResponses { get; set; } = new();
}