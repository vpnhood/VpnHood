using VpnHood.Core.Client.Devices.Abstractions;
using VpnHood.Test.Device;

namespace VpnHood.Test.Tests;

[TestClass]
public class DeviceExtensionsTest : TestBase
{
    // A device that does not support the binding was logged as such and called anyway, which it refuses with an
    // error of its own. No call is made
    [TestMethod]
    public void A_device_without_the_binding_gets_no_call()
    {
        using var device = new TestDevice(TestHelper, _ => new TestNullVpnAdapter()) {
            IsBindProcessToVpnSupported = false
        };

        Assert.IsFalse(device.TryBindProcessToVpn(true));
        Assert.AreEqual(0, device.BindProcessToVpnTrueCount);
    }
}
