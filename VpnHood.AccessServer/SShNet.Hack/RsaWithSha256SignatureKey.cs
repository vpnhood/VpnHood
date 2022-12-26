using Renci.SshNet.Common;
using Renci.SshNet.Security;
using Renci.SshNet.Security.Cryptography;

namespace VpnHood.AccessServer.SShNet.Hack;



public class RsaWithSha256SignatureKey : RsaKey
{
    public RsaWithSha256SignatureKey(BigInteger modulus, BigInteger exponent, BigInteger d, BigInteger p, BigInteger q,
        BigInteger inverseQ) : base(modulus, exponent, d, p, q, inverseQ)
    {
    }

    private RsaSha256DigitalSignature? _digitalSignature;

    protected override DigitalSignature DigitalSignature => 
        _digitalSignature ??= new RsaSha256DigitalSignature(this);

    public override string ToString()
    {
        return "rsa-sha2-256";
    }
}