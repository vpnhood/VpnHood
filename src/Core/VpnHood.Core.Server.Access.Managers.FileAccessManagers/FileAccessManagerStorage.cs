using System.Security.Cryptography.X509Certificates;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Server.Access.Managers.FileAccessManagers.Services;

namespace VpnHood.Core.Server.Access.Managers.FileAccessManagers;

// what a file access manager read in its storage folder before it was made, with the options it read it for
public class FileAccessManagerStorage
{
    public required string StoragePath { get; init; }
    public required FileAccessManagerOptions Options { get; init; }
    public required X509Certificate2 DefaultCert { get; init; }
    public required ServerToken ServerToken { get; init; }
    public required SessionService SessionService { get; init; }
}
