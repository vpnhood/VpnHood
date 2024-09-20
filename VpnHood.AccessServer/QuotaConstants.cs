namespace VpnHood.AccessServer;

public static class QuotaConstants
{
    public static int ProjectCount { get; set; } = 2;
    public static int ServerCount { get; set; } = 10;
    public static int AccessTokenCount { get; set; } = 2000;
    public static int ServerFarmCount { get; set; } = 10;
    public static int AccessPointCount { get; set; } = 10;
    public static int TeamUserCount { get; set; } = 3;
    public static int CertificateCount { get; set; } = ServerFarmCount * 5;
    public static int FarmTokenRepoCount { get; set; } = 10;
    public static int ClientFilterCount { get; set; } = 1;
    public static TimeSpan UsageQueryTimeSpanFree { get; set; } = TimeSpan.FromDays(33);
    public static TimeSpan UsageQueryTimeSpanPremium { get; set; } = TimeSpan.FromDays(33 * 6);
}