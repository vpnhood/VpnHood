using VpnHood.AccessServer.Authorization.Models;

namespace VpnHood.AccessServer.Security
{
    public static class Permissions
    {
        public static Permission ExportCertificate { get; } = new(1, nameof(ExportCertificate));
        public static Permission CreateProject { get; } = new(2, nameof(CreateProject));
        public static Permission ExportToken { get; } = new(4, nameof(ExportToken));
        public static Permission ReadAccessToken { get; } = new(5, nameof(ReadAccessToken));
        public static Permission ReadUser { get; set; } = new(6, nameof(ReadUser));
        public static Permission WriteUser { get; set; } = new(7, nameof(WriteUser));
        public static Permission WriteServer { get; set; } = new(8, nameof(WriteServer));
        public static Permission ReadServer { get; set; } = new(9, nameof(ReadServer));
        public static Permission ReadServerConfig { get; set; } = new(10, nameof(ReadServerConfig));
        public static Permission AccessPointWrite { get; set; } = new(11, nameof(AccessPointWrite));
        public static Permission AccessPointRead { get; set; } = new(11, nameof(AccessPointRead));
        public static Permission AccessTokenReadAccessKey { get; set; } = new(12, nameof(AccessTokenReadAccessKey));


        public static Permission[] All { get; } =
        {
            ExportCertificate,
            CreateProject,
            ExportToken,
            ReadAccessToken,
            ReadUser,
            WriteUser,
            WriteServer,
            ReadServer,
            ReadServerConfig,
            AccessPointWrite,
            AccessPointRead,
            AccessTokenReadAccessKey
        };

    }
}