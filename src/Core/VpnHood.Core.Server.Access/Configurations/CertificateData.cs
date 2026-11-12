namespace VpnHood.Core.Server.Access.Configurations;

/// <summary>
/// Represents a TLS certificate of the server's, as its configuration carries it.
/// </summary>
public class CertificateData
{
    /// <summary>Gets the certificate's common name: the host name it is for.</summary>
    public required string CommonName { get; init; }

    /// <summary>Gets the certificate with its private key, as PKCS#12 with no password.</summary>
    public required byte[] RawData { get; init; }
}