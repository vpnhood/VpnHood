using System;
using System.Security.Cryptography;
using Renci.SshNet.Common;
using Renci.SshNet.Security.Cryptography;
using Renci.SshNet.Security.Cryptography.Ciphers;

namespace VpnHood.AccessServer.SShNet.Hack;

/// <summary>
/// Based on https://github.com/sshnet/SSH.NET/blob/1d5d58e17c68a2f319c51e7f938ce6e964498bcc/src/Renci.SshNet/Security/Cryptography/RsaDigitalSignature.cs#L12
///
/// With following changes:
///
/// - OID changed to sha2-256
/// - hash changed from sha1 to sha2-256
/// </summary>
public class RsaSha256DigitalSignature : CipherDigitalSignature, IDisposable
{
    private readonly HashAlgorithm _hash;

    public RsaSha256DigitalSignature(RsaWithSha256SignatureKey rsaKey)
        // custom OID
        : base(new ObjectIdentifier(2, 16, 840, 1, 101, 3, 4, 2, 1), new RsaCipher(rsaKey))
    {
        // custom
        _hash = SHA256.Create();
    }

    protected override byte[] Hash(byte[] input)
    {
        return _hash.ComputeHash(input);
    }

    private bool _isDisposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_isDisposed)
            return;

        if (disposing)
        {
            var hash = _hash;
            hash.Dispose();

            _isDisposed = true;
        }
    }

    ~RsaSha256DigitalSignature()
    {
        Dispose(false);
    }
}
