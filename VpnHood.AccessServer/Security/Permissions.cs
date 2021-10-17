using VpnHood.AccessServer.Authorization.Models;

namespace VpnHood.AccessServer.Security
{
    public static class Permissions
    {
        public static Permission ProjectCreate { get; } = new(10, nameof(ProjectCreate));
        public static Permission ProjectRead { get; } = new(11, nameof(ProjectRead));
        public static Permission ProjectWrite { get; } = new(12, nameof(ProjectWrite));
        public static Permission ProjectList { get; } = new(13, nameof(ProjectList));
        public static Permission CertificateRead { get; set; } = new(21, nameof(CertificateRead));
        public static Permission CertificateWrite { get; set; } = new(22, nameof(CertificateWrite));
        public static Permission CertificateExport { get; } = new(23, nameof(CertificateExport));
        public static Permission AccessTokenRead { get; } = new(30, nameof(AccessTokenRead));
        public static Permission AccessTokenWrite { get; } = new(31, nameof(AccessTokenWrite));
        public static Permission AccessTokenReadAccessKey { get; set; } = new(32, nameof(AccessTokenReadAccessKey));
        public static Permission UserRead { get; set; } = new(40, nameof(UserRead));
        public static Permission UserWrite { get; set; } = new(41, nameof(UserWrite));
        public static Permission ServerRead { get; set; } = new(50, nameof(ServerRead));
        public static Permission ServerWrite { get; set; } = new(51, nameof(ServerWrite));
        public static Permission ServerReadConfig { get; set; } = new(52, nameof(ServerReadConfig));
        public static Permission AccessPointRead { get; set; } = new(60, nameof(AccessPointRead));
        public static Permission AccessPointWrite { get; set; } = new(61, nameof(AccessPointWrite));
        public static Permission AccessPointGroupRead { get; set; } = new(70, nameof(AccessPointGroupRead));
        public static Permission AccessPointGroupWrite { get; set; } = new(71, nameof(AccessPointGroupWrite));
        public static Permission ClientRead { get; set; } = new(80, nameof(ClientRead));

        public static Permission[] All { get; } =
        {
            ProjectCreate,
            ProjectRead,
            ProjectWrite,
            ProjectList,
            CertificateRead,
            CertificateWrite,
            CertificateExport,
            AccessTokenRead,
            AccessTokenWrite,
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
            ClientRead,
        };

    }
}