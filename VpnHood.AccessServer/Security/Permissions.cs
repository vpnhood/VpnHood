namespace VpnHood.AccessServer.Security;

public static class Permissions
{
    public const string ProjectRead = nameof(ProjectRead);
    public const string ProjectWrite = nameof(ProjectWrite);
    public const string ProjectList = nameof(ProjectList);
    public const string HostOrderWrite = nameof(HostOrderWrite);
    public const string CertificateRead = nameof(CertificateRead);
    public const string CertificateWrite = nameof(CertificateWrite);
    public const string CertificateExport = nameof(CertificateExport);
    public const string AccessTokenWrite = nameof(AccessTokenWrite);
    public const string AccessTokenReadAccessKey = nameof(AccessTokenReadAccessKey);
    public const string ServerWrite = nameof(ServerWrite);
    public const string ServerInstall = nameof(ServerInstall);
    public const string ServerFarmWrite = nameof(ServerFarmWrite);
    public const string IpLockWrite = nameof(IpLockWrite);
    public const string ProjectCreate = nameof(ProjectCreate);
    public const string Sync = nameof(Sync);
}