namespace VpnHood.AccessServer.Dtos;

public class ServerFarmCreateParams
{
    public string? ServerFarmName { get; set; }
    public Guid? ServerProfileId { get; set; }
    public Guid? CertificateId { get; set; }
    public bool UseHostName { get; set; }
    public Uri? TokenUrl { get; set; }
}