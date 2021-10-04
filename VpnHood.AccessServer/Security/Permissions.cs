using VpnHood.AccessServer.Authorization.Models;

namespace VpnHood.AccessServer.Security
{
    public static class Permissions
    {
        public static Permission ExportCertificate { get; } = new(1, nameof(ExportCertificate));
        public static Permission CreateProject { get; } = new(2, nameof(CreateProject));
        public static Permission ExportToken { get; } = new(4, nameof(ExportToken));
        public static Permission ListTokens { get; } = new(5, nameof(ListTokens));
        public static Permission ReadUser { get; set; } = new(6, nameof(ReadUser));
        public static Permission UpdateUser { get; set; } = new(7, nameof(UpdateUser));


        public static Permission[] All { get; } =
        {
            ExportCertificate,
            CreateProject,
            ExportToken,
            ListTokens,
            ReadUser,
            UpdateUser
        };

    }
}