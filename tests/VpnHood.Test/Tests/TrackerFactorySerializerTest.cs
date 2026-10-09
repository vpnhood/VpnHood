using System.Text.Json;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Tracking;

namespace VpnHood.Test.Tests;

// A tracker factory crosses to the VPN service as its type and its public properties.
[TestClass]
public class TrackerFactorySerializerTest : TestBase
{
    [TestMethod]
    public void A_factory_comes_back_with_its_settings()
    {
        var trackerFactoryInfo = TrackerFactorySerializer.Serialize(new Ga4TrackerFactory { MeasurementId = "G-TEST" });

        var trackerFactory = TrackerFactorySerializer.TryDeserialize(trackerFactoryInfo);
        Assert.IsInstanceOfType<Ga4TrackerFactory>(trackerFactory);
        Assert.AreEqual("G-TEST", ((Ga4TrackerFactory)trackerFactory).MeasurementId);
    }

    [TestMethod]
    public void A_type_this_process_lacks_makes_none()
    {
        var trackerFactory = TrackerFactorySerializer.TryDeserialize(new TrackerFactoryInfo {
            AssemblyQualifiedName = "NoSuch.TrackerFactory, NoSuch.Assembly",
            Settings = JsonSerializer.SerializeToElement(new { })
        });

        Assert.IsNull(trackerFactory);
    }
}
