namespace VpnHood.AccessServer;

public static class QuotaConstants
{
    public static int ProjectCount { get; set; } = 3;
    public static int ServerCount { get; set; } = 3;
    public static int AccessTokenCount { get; set; } = 1000;
    public static int AccessPointGroupCount { get; set; } = 100;
    public static int AccessPointCount { get; set; } = 100;
    public static int CertificateCount { get; set; } = AccessPointGroupCount;
}