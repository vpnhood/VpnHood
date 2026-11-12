using System.Security.Cryptography.X509Certificates;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Server.Access.Managers.FileAccessManagers.Services;

namespace VpnHood.Core.Server.Access.Managers.FileAccessManagers;

/// <summary>
/// Represents what a file access manager read in its storage folder before it was made, with the options it read it
/// for.
/// </summary>
public class FileAccessManagerStorage
{
    /// <summary>Gets the storage folder.</summary>
    public required string StoragePath { get; init; }

    /// <summary>Gets the options, with the server secret read or made in the folder where they had none.</summary>
    public required FileAccessManagerOptions Options { get; init; }

    /// <summary>Gets the server's certificate, read or made.</summary>
    public required X509Certificate2 DefaultCert { get; init; }

    /// <summary>Gets the server token the access keys carry.</summary>
    public required ServerToken ServerToken { get; init; }

    /// <summary>Gets the sessions, as their files gave them.</summary>
    public required SessionService SessionService { get; init; }
}
