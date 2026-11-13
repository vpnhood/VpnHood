using VpnHood.Net.Toolkit.Graphics;

namespace VpnHood.Test.Tests;

[TestClass]
public class VhColorTest
{
    [TestMethod]
    public void A_color_is_parsed_with_or_without_its_hash_and_alpha()
    {
        Assert.AreEqual(VhColor.FromRgb(0xFF, 0x80, 0x01), VhColor.Parse("#FF8001"));
        Assert.AreEqual(VhColor.FromRgb(0xFF, 0x80, 0x01), VhColor.Parse("ff8001"));
        Assert.AreEqual(VhColor.FromArgb(0x10, 0xFF, 0x80, 0x01), VhColor.Parse("#10FF8001"));
        Assert.AreEqual(VhColor.FromArgb(0x10, 0xFF, 0x80, 0x01), VhColor.Parse("10ff8001"));
    }

    [TestMethod]
    // a hash anywhere but in front
    [DataRow("FF#00#00")]
    [DataRow("##FF0000")]
    [DataRow("FF0000#")]
    // a sign, a space or a letter that is no hexadecimal digit
    [DataRow("-10000")]
    [DataRow("+10000")]
    [DataRow(" FF0000")]
    [DataRow("#GG0000")]
    [DataRow("#FF00")]
    [DataRow("")]
    public void What_is_no_color_is_refused(string value)
    {
        Assert.ThrowsExactly<FormatException>(() => VhColor.Parse(value));
        Assert.IsFalse(VhColor.TryParse(value, out var color));
        Assert.AreEqual(default, color);
    }

    [TestMethod]
    public void TryParse_takes_null_as_no_color()
    {
        Assert.IsFalse(VhColor.TryParse(null, out _));
        Assert.IsTrue(VhColor.TryParse("#000000", out var black));
        Assert.AreEqual(VhColor.FromRgb(0, 0, 0), black);
    }
}
