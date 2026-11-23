using System.Globalization;
using VpnHood.Core.Common.Tokens;

namespace VpnHood.Test.Tests;

[TestClass]
public class ServerLocationInfoTest
{
    // A country code was made upper case by the system's language, so on a Turkish system "ir" became "İR", the code of
    // no country
    [TestMethod]
    public void A_country_code_is_upper_case_whatever_the_systems_language()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
        try {
            Assert.AreEqual("IR", ServerLocationInfo.Parse("ir/Tehran").CountryCode);
        }
        finally {
            CultureInfo.CurrentCulture = culture;
        }
    }
}
