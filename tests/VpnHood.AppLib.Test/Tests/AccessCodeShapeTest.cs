using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Test.Tests;

/// <summary>
/// The access-code SHAPE rule, driven by vectors the PORTAL's unit suite reads as well
/// (Fixtures/access-code-vectors.json, twinned at VpnHood.WHMCS.Iap/tests/unit/).
/// <para>
/// The two validators drifted once: the portal demanded all digits while this one allows letters
/// after the checksum, so a perfectly good code would have been refused at the account's door. A
/// person cannot use a code one side accepts and the other refuses, so the vectors are the contract
/// between them — the file is copied rather than referenced, because the repos ship separately, and
/// an edit on either side fails the other's tests.
/// </para>
/// Shape is never validity: only the access server can say whether a code works, and it says so at
/// use time (keyring plan §5).
/// </summary>
[TestClass]
public class AccessCodeShapeTest
{
    // ReSharper disable once ClassNeverInstantiated.Local
    [SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Local")]
    private class Vector
    {
        public string Code { get; init; } = "";
        public string Why { get; init; } = "";
    }

    private class Vectors
    {
        public Vector[] Valid { get; init; } = [];
        public Vector[] Invalid { get; init; } = [];
    }

    private static Vectors LoadVectors()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "access-code-vectors.json");
        Assert.IsTrue(File.Exists(path), $"the shared vectors are missing: {path}");
        return JsonSerializer.Deserialize<Vectors>(File.ReadAllText(path),
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? throw new InvalidOperationException("the shared vectors could not be read");
    }

    [TestMethod]
    public void Every_shared_valid_vector_is_accepted()
    {
        foreach (var vector in LoadVectors().Valid) {
            var validated = AccessCodeUtils.TryValidate(vector.Code);
            Assert.IsNotNull(validated, $"refused a valid code ({vector.Why}): {vector.Code}");
            Assert.AreEqual(20, validated.Length, $"validated to the wrong length: {vector.Code}");
        }
    }

    [TestMethod]
    public void Every_shared_invalid_vector_is_refused()
    {
        foreach (var vector in LoadVectors().Invalid)
            Assert.IsNull(AccessCodeUtils.TryValidate(vector.Code),
                $"accepted an invalid code ({vector.Why}): {vector.Code}");
    }

    [TestMethod]
    public void Letters_after_the_checksum_are_legal()
    {
        // the exact rule the two sides disagreed on: the checksum sums character codes, so the
        // eighteen characters after it are alphanumeric and not digits
        Assert.IsNotNull(AccessCodeUtils.TryValidate("19ABCDEFGHIJKLMNOPQR"));
    }

    [TestMethod]
    public void Separators_never_make_a_second_identity()
    {
        // the account fingerprints codes to reject and un-reject them; the same code written two
        // ways has to be one code on both sides of that
        Assert.AreEqual(AccessCodeUtils.TryValidate("12125638402680515648"),
            AccessCodeUtils.TryValidate("1212-5638-4026-8051-5648"));
    }
}
