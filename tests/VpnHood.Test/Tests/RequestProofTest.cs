using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using VpnHood.Core.Tunneling.ProbeResistance;

namespace VpnHood.Test.Tests;

[TestClass]
public class RequestProofTest
{
    private static readonly byte[] Secret = Enumerable.Range(0, 16).Select(x => (byte)x).ToArray();
    private static readonly DateTimeOffset Now = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [TestMethod]
    public void Proof_is_bound_to_farm_and_version_without_a_host_or_key_identifier()
    {
        var proof = new RequestProof(Secret);
        var token = proof.Create(15, Now);
        Assert.IsTrue(proof.Verify(token, 15, Now));
        Assert.IsFalse(proof.Verify(token, 14, Now));
        Assert.IsFalse(new RequestProof(new byte[16]).Verify(token, 15, Now));
        Assert.HasCount(32, RequestProof.DeriveSigningKey(Secret));

        var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        using var claims = JsonDocument.Parse(Convert.FromBase64String(payload.PadRight((payload.Length + 3) / 4 * 4, '=')));
        CollectionAssert.AreEquivalent(new[] { "purpose", "version", "iat", "exp" },
            claims.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
    }

    [TestMethod]
    [DataRow(-331, false)]
    [DataRow(-330, false)]
    [DataRow(-329, true)]
    [DataRow(0, true)]
    [DataRow(30, true)]
    [DataRow(31, false)]
    public void Signed_times_have_a_fixed_lifetime_and_small_tolerance(int ageSeconds, bool accepted)
    {
        var proof = new RequestProof(Secret);
        var token = proof.Create(15, Now.AddSeconds(ageSeconds));
        Assert.AreEqual(accepted, proof.Verify(token, 15, Now));
    }

    [TestMethod]
    [DataRow("purpose", "another-purpose")]
    [DataRow("version", "15")]
    [DataRow("iat", "invalid-time")]
    [DataRow("exp", "invalid-time")]
    public void Signed_claims_are_checked(string claim, string value)
    {
        var claims = CreateClaims();
        claims[claim] = value;
        var token = Sign("{\"alg\":\"HS256\",\"typ\":\"JWT\"}", claims.ToJsonString());
        Assert.IsFalse(new RequestProof(Secret).Verify(token, 15, Now));
    }

    [TestMethod]
    [DataRow("host")]
    [DataRow("kid")]
    [DataRow("jti")]
    public void Additional_claims_including_the_unreleased_host_shape_are_rejected(string claim)
    {
        var claims = CreateClaims();
        claims[claim] = "unused";
        var token = Sign("{\"alg\":\"HS256\",\"typ\":\"JWT\"}", claims.ToJsonString());
        Assert.IsFalse(new RequestProof(Secret).Verify(token, 15, Now));
    }

    [TestMethod]
    [DataRow("purpose")]
    [DataRow("version")]
    [DataRow("iat")]
    [DataRow("exp")]
    public void Every_claim_is_required(string claim)
    {
        var claims = CreateClaims();
        claims.Remove(claim);
        var token = Sign("{\"alg\":\"HS256\",\"typ\":\"JWT\"}", claims.ToJsonString());
        Assert.IsFalse(new RequestProof(Secret).Verify(token, 15, Now));
    }

    [TestMethod]
    [DataRow("{\"alg\":\"none\",\"typ\":\"JWT\"}")]
    [DataRow("{\"alg\":\"HS384\",\"typ\":\"JWT\"}")]
    [DataRow("{\"alg\":\"HS256\",\"typ\":\"other\"}")]
    [DataRow("{\"alg\":\"HS256\",\"typ\":\"JWT\",\"alg\":\"HS256\"}")]
    public void Signed_headers_cannot_change_the_algorithm_or_type(string header)
    {
        var token = Sign(header, CreateClaims().ToJsonString());
        Assert.IsFalse(new RequestProof(Secret).Verify(token, 15, Now));
    }

    [TestMethod]
    public void Lifetime_cannot_be_extended_and_duplicate_claims_are_rejected()
    {
        var claims = CreateClaims();
        claims["exp"] = Now.AddDays(1).ToUnixTimeSeconds();
        var token = Sign("{\"alg\":\"HS256\",\"typ\":\"JWT\"}", claims.ToJsonString());
        var proof = new RequestProof(Secret);
        Assert.IsFalse(proof.Verify(token, 15, Now));
        var duplicate = CreateClaims().ToJsonString().TrimEnd('}') + ",\"purpose\":\"vpnhood-request\"}";
        Assert.IsFalse(proof.Verify(Sign("{\"alg\":\"HS256\",\"typ\":\"JWT\"}", duplicate),
            15, Now));
    }

    [TestMethod]
    public void Malformed_and_oversized_tokens_fail_without_throwing()
    {
        var proof = new RequestProof(Secret);
        var token = proof.Create(15, Now);
        string[] invalid = ["", ".", "a.b.c", token + ".extra", new string('a', 2049),
            token[..^43] + new string('!', 43), token[..^43] + new string('a', 43)];
        foreach (var value in invalid)
            Assert.IsFalse(proof.Verify(value, 15, Now));
    }

    private static JsonObject CreateClaims() => new() {
        ["purpose"] = RequestProof.Purpose,
        ["version"] = 15,
        ["iat"] = Now.ToUnixTimeSeconds(),
        ["exp"] = Now.Add(RequestProof.Lifetime).ToUnixTimeSeconds()
    };

    // Signing altered JSON tests claim validation independently from a corrupt signature.
    private static string Sign(string header, string payload)
    {
        var signed = Encode(Encoding.UTF8.GetBytes(header)) + "." + Encode(Encoding.UTF8.GetBytes(payload));
        return signed + "." + Encode(HMACSHA256.HashData(RequestProof.DeriveSigningKey(Secret),
            Encoding.ASCII.GetBytes(signed)));
    }

    private static string Encode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
