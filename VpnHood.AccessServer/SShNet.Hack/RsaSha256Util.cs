using System;
using System.Reflection;
using Renci.SshNet;
using Renci.SshNet.Security;

namespace VpnHood.AccessServer.SShNet.Hack;

/// <summary>
/// Utility class which allows ssh.net to connect to servers using ras-sha2-256
/// </summary>
public static class RsaSha256Util
{
    public static void SetupConnection(ConnectionInfo connection)
    {
        connection.HostKeyAlgorithms["rsa-sha2-256"] = data => new KeyHostAlgorithm("rsa-sha2-256", new RsaKey(), data);
    }

    /// <summary>
    /// Converts key file to rsa key with sha2-256 signature
    /// Due to lack of constructor: https://github.com/sshnet/SSH.NET/blob/bc99ada7da3f05f50d9379f2644941d91d5bf05a/src/Renci.SshNet/PrivateKeyFile.cs#L86
    /// We do that in place
    /// </summary>
    /// <param name="keyFile"></param>
    /// <exception cref="ArgumentNullException"></exception>
    public static void ConvertToKeyWithSha256Signature(PrivateKeyFile keyFile)
    {
        var oldKeyHostAlgorithm = keyFile.HostKey as KeyHostAlgorithm;
        if (oldKeyHostAlgorithm == null)
        {
            throw new ArgumentNullException(nameof(oldKeyHostAlgorithm));
        }
        var oldRsaKey = oldKeyHostAlgorithm.Key as RsaKey;
        if (oldRsaKey == null)
        {
            throw new ArgumentNullException(nameof(oldRsaKey));
        }

        var newRsaKey = new RsaWithSha256SignatureKey(oldRsaKey.Modulus, oldRsaKey.Exponent, oldRsaKey.D, oldRsaKey.P, oldRsaKey.Q,
            oldRsaKey.InverseQ);

        UpdatePrivateKeyFile(keyFile, newRsaKey);
    }

    private static void UpdatePrivateKeyFile(PrivateKeyFile keyFile, RsaWithSha256SignatureKey key)
    {
        var keyHostAlgorithm = new KeyHostAlgorithm(key.ToString(), key);

        var hostKeyProperty = typeof(PrivateKeyFile).GetProperty(nameof(PrivateKeyFile.HostKey))
                              ?? throw new Exception("SSh.Net: Could not get HostKey of PrivateKeyFile");
        hostKeyProperty.SetValue(keyFile, keyHostAlgorithm);

        var keyField = typeof(PrivateKeyFile).GetField("_key", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new Exception("SSh.Net: Could not get _key of PrivateKeyFile");

        keyField.SetValue(keyFile, key);
    }
}
