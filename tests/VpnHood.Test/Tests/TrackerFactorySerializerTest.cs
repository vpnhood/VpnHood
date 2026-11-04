using System.Text.Json;
using Ga4.Trackers;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Tracking;

namespace VpnHood.Test.Tests;

// A tracker factory crosses to the VPN engine as its kind and settings: GA4's as data any engine can read,
// another as the .NET type a .NET engine makes again.
[TestClass]
public class TrackerFactorySerializerTest : TestBase
{
    [TestMethod]
    public void Ga4_crosses_as_its_kind_and_measurement_id()
    {
        var trackerFactoryInfo = TrackerFactorySerializer.Serialize(new Ga4TrackerFactory { MeasurementId = "G-TEST" });
        Assert.AreEqual(TrackerKinds.Ga4, trackerFactoryInfo.Kind);
        Assert.AreEqual("G-TEST", trackerFactoryInfo.Settings.GetProperty("MeasurementId").GetString());

        var trackerFactory = TrackerFactorySerializer.TryDeserialize(trackerFactoryInfo);
        Assert.IsInstanceOfType<Ga4TrackerFactory>(trackerFactory);
        Assert.AreEqual("G-TEST", ((Ga4TrackerFactory)trackerFactory).MeasurementId);
    }

    [TestMethod]
    public void Another_factory_comes_back_from_its_dotnet_type()
    {
        var trackerFactoryInfo = TrackerFactorySerializer.Serialize(new TestTrackerFactory { Key = "k1" });
        Assert.AreEqual(TrackerKinds.DotNet, trackerFactoryInfo.Kind);

        var trackerFactory = TrackerFactorySerializer.TryDeserialize(trackerFactoryInfo);
        Assert.IsInstanceOfType<TestTrackerFactory>(trackerFactory);
        Assert.AreEqual("k1", ((TestTrackerFactory)trackerFactory).Key);
    }

    [TestMethod]
    public void A_type_this_process_lacks_makes_none()
    {
        var trackerFactory = TrackerFactorySerializer.TryDeserialize(new TrackerFactoryInfo {
            Kind = TrackerKinds.DotNet,
            Settings = JsonSerializer.SerializeToElement(new {
                Type = "NoSuch.TrackerFactory, NoSuch.Assembly",
                Settings = new { }
            })
        });

        Assert.IsNull(trackerFactory);
    }

    [TestMethod]
    public void A_kind_this_engine_lacks_makes_none()
    {
        var trackerFactory = TrackerFactorySerializer.TryDeserialize(new TrackerFactoryInfo {
            Kind = "no-such-kind-yet",
            Settings = JsonSerializer.SerializeToElement(new { })
        });

        Assert.IsNull(trackerFactory);
    }

    [TestMethod]
    public void Options_from_before_kinds_make_no_tracker()
    {
        var trackerFactoryInfo = JsonSerializer.Deserialize<TrackerFactoryInfo>(
            """{ "AssemblyQualifiedName": "VpnHood.X, VpnHood.X", "Settings": {} }""");

        Assert.IsNotNull(trackerFactoryInfo);
        Assert.IsNull(TrackerFactorySerializer.TryDeserialize(trackerFactoryInfo));
    }

    public class TestTrackerFactory : ITrackerFactory
    {
        public string? Key { get; init; }

        public ITracker CreateTracker(TrackerCreateParams createParams) =>
            NullTrackerFactory.CreateNullTracker(createParams);
    }
}
