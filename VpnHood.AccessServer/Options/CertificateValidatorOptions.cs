namespace VpnHood.AccessServer.Options;

public class CertificateValidatorOptions
{
    public TimeSpan Due { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan ExpirationThreshold { get; set; } = TimeSpan.FromDays(30);
    public int MaxRetry { get; set; } = 5;
}