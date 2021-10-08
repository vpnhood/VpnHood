using VpnHood.AccessServer.Authorization.Models;

namespace VpnHood.AccessServer.Security
{
    public static class Permissions
    {
        public static Permission CertificateAdd { get; set; } = new(12, nameof(CertificateAdd));
        public static Permission CertificateExport { get; } = new(13, nameof(CertificateExport));
        public static Permission ProjectCreate { get; } = new(20, nameof(ProjectCreate));
        public static Permission AccessTokenRead { get; } = new(30, nameof(AccessTokenRead));
        public static Permission AccessTokenReadAccessKey { get; set; } = new(32, nameof(AccessTokenReadAccessKey));
        public static Permission UserRead { get; set; } = new(40, nameof(UserRead));
        public static Permission UserWrite { get; set; } = new(41, nameof(UserWrite));
        public static Permission ServerRead { get; set; } = new(50, nameof(ServerRead));
        public static Permission ServerWrite { get; set; } = new(51, nameof(ServerWrite));
        public static Permission ServerReadConfig { get; set; } = new(52, nameof(ServerReadConfig));
        public static Permission AccessPointRead { get; set; } = new(60, nameof(AccessPointRead));
        public static Permission AccessPointWrite { get; set; } = new(61, nameof(AccessPointWrite));
        public static Permission AccessPointGroupRead { get; set; } = new(14, nameof(AccessPointGroupRead));
        public static Permission AccessPointGroupWrite { get; set; } = new(71, nameof(AccessPointGroupWrite));

        
        public static Permission[] All { get; } =
        {
            CertificateAdd,
            CertificateExport,
            ProjectCreate,
            AccessTokenRead,
            AccessTokenReadAccessKey,
            UserRead,
            UserWrite,
            ServerRead,
            ServerWrite,
            ServerReadConfig,
            AccessPointRead,
            AccessPointWrite,
            AccessPointGroupRead,
            AccessPointGroupWrite,
        };

    }
}