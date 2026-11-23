using System.Security.Cryptography;
using VpnHood.Core.Common.Tokens;

namespace VpnHood.Test.Tests;

[TestClass]
public class ServerTokenTest : TestBase
{
    // The random IV was as long as the secret, where AES takes 16 bytes alone, so a token with a 32-byte secret, an
    // AES-256 key, could not be encrypted for its update URLs
    [TestMethod]
    [DataRow(16)]
    [DataRow(24)]
    [DataRow(32)]
    public void A_token_encrypts_and_decrypts_with_a_secret_of_any_AES_length(int secretLength)
    {
        var secret = RandomNumberGenerator.GetBytes(secretLength);
        var serverToken = new ServerToken {
            HostName = "vpn.example",
            HostPort = 443,
            Secret = secret,
            CreatedTime = DateTime.UtcNow,
            IsValidHostName = true
        };

        var encrypted = serverToken.Encrypt();
        var decrypted = ServerToken.Decrypt(secret, encrypted);
        Assert.AreEqual("vpn.example", decrypted.HostName);
        Assert.AreEqual(16, Convert.FromBase64String(encrypted.Split('.')[0]).Length, "The IV is not AES's block.");
    }
}
