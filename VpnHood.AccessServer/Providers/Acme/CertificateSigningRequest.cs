namespace VpnHood.AccessServer.Providers.Acme;

public class CertificateSigningRequest
{
    public string? CommonName { get; set; }
    public string? Organization { get; init; }
    public string? OrganizationUnit { get; init; }
    public string? LocationCountry { get; init; }
    public string? LocationState { get; init; }
    public string? LocationCity { get; init; }

    public string BuildSubjectName()
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(CommonName)) parts.Add($"CN={CommonName}");
        if (!string.IsNullOrWhiteSpace(Organization)) parts.Add($"O={Organization}");
        if (!string.IsNullOrWhiteSpace(OrganizationUnit)) parts.Add($"OU={OrganizationUnit}");
        if (!string.IsNullOrWhiteSpace(LocationCountry)) parts.Add($"C={LocationCountry}");
        if (!string.IsNullOrWhiteSpace(LocationState)) parts.Add($"ST={LocationState}");
        if (!string.IsNullOrWhiteSpace(LocationCity)) parts.Add($"L={LocationCity}");
        return string.Join(", ", parts);
    }
}