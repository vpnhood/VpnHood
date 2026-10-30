using VpnHood.Core.Client.ConnectorServices;

namespace VpnHood.Test.Tests;

[TestClass]
public class ClientRequestSignerTest
{
    [TestMethod]
    [DataRow(0, false)]
    [DataRow(20, false)]
    [DataRow(-20, false)]
    [DataRow(172800, true)]
    [DataRow(-172800, true)]
    [DataRow(7200, true)]
    public void Clock_correction_requires_a_meaningful_offset_without_a_cap(int seconds, bool accepted)
    {
        var signer = new ClientRequestSigner(new byte[16], "localhost");
        var serverTime = DateTimeOffset.UtcNow.AddSeconds(seconds);
        Assert.AreEqual(0L, signer.ClockCorrectionVersion);
        Assert.IsFalse(signer.TryCorrectClock(null, 0));
        Assert.AreEqual(0L, signer.ClockCorrectionVersion);
        Assert.AreEqual(accepted, signer.TryCorrectClock(serverTime, 0));
        var clockCorrectionVersion = accepted ? 1L : 0;
        Assert.AreEqual(clockCorrectionVersion, signer.ClockCorrectionVersion);
        Assert.IsFalse(signer.TryCorrectClock(serverTime, clockCorrectionVersion));
        Assert.AreEqual(clockCorrectionVersion, signer.ClockCorrectionVersion);
        Assert.IsTrue(signer.TryCorrectClock(serverTime.AddHours(1), clockCorrectionVersion));
        Assert.AreEqual(clockCorrectionVersion + 1, signer.ClockCorrectionVersion);

        // A late rejection can retry the current proof, but cannot overwrite a newer correction.
        Assert.IsTrue(signer.TryCorrectClock(serverTime.AddDays(-10), clockCorrectionVersion));
        Assert.IsTrue(signer.TryCorrectClock(null, clockCorrectionVersion));
        Assert.AreEqual(clockCorrectionVersion + 1, signer.ClockCorrectionVersion);
        Assert.IsFalse(signer.TryCorrectClock(serverTime.AddHours(1), clockCorrectionVersion + 1));
        Assert.AreEqual(clockCorrectionVersion + 1, signer.ClockCorrectionVersion);
        Assert.IsTrue(signer.TryCorrectClock(serverTime, clockCorrectionVersion + 1));
        Assert.AreEqual(clockCorrectionVersion + 2, signer.ClockCorrectionVersion);
    }
}
