using VpnHood.AccessServer.Authorization.Models;

namespace VpnHood.AccessServer.Security
{
    public static class Permissions
    {
        public static Permission ExportCertificate { get; }= new (1, nameof(ExportCertificate));
        public static Permission CreateProject { get; }= new (2, nameof(CreateProject));

        public static Permission[] All { get; } = {ExportCertificate, CreateProject};
    }
}