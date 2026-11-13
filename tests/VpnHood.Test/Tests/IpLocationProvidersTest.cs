using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using VpnHood.Net.IpLocations;
using VpnHood.Net.IpLocations.Providers.Offlines;
using VpnHood.Net.IpLocations.Providers.Onlines;
using VpnHood.Net.Toolkit.Assets;
using VpnHood.Net.Toolkit.Exceptions;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Test.Tests;

[TestClass]
public class IpLocationProvidersTest : TestBase
{
    private static readonly IPAddress IranAddress = IPAddress.Parse("5.2.0.1");

    private static IpLocation CreateLocation()
    {
        return new IpLocation {
            IpAddress = IPAddress.Loopback, CountryName = "United States", CountryCode = "US", RegionName = null,
            CityName = null
        };
    }

    // a zip of two countries' ranges, as the parser builds it on a machine of any language
    private async Task<byte[]> BuildRangeZip()
    {
        const string csv = "\"16777216\",\"16777471\",\"US\",\"United States of America\"\n" +
                           "\"84017152\",\"84017407\",\"IR\",\"Iran (Islamic Republic of)\"\n";
        var zipPath = Path.Combine(TestHelper.WorkingPath, $"ip-ranges_{Guid.NewGuid()}.zip");
        Directory.CreateDirectory(TestHelper.WorkingPath);
        await using (var csvStream = new MemoryStream(Encoding.ASCII.GetBytes(csv)))
            await Ip2LocationDbParser.BuildLocalIpRangeLocationDb(csvStream, zipPath, TestCt);

        return await File.ReadAllBytesAsync(zipPath, TestCt);
    }

    [TestMethod]
    public async Task Composite_passes_the_callers_cancellation_on()
    {
        // one provider, which waits until it is canceled
        using var composite = new CompositeIpLocationProvider(NullLogger.Instance, [
            new TestLocationProvider(async cancellationToken => {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return CreateLocation();
            })
        ]);

        // the caller cancels: that is the answer, not "no provider found the location"
        using var locationCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        locationCts.CancelAfter(100);
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            composite.GetLocation(IPAddress.Loopback, locationCts.Token));

        using var currentCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        currentCts.CancelAfter(100);
        await Assert.ThrowsAsync<OperationCanceledException>(() => composite.GetCurrentLocation(currentCts.Token));
    }

    [TestMethod]
    public async Task Composite_goes_on_to_the_next_provider_when_one_runs_out_of_time()
    {
        using var composite = new CompositeIpLocationProvider(NullLogger.Instance, [
            new TestLocationProvider(async cancellationToken => {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return CreateLocation();
            }),
            new TestLocationProvider(_ => Task.FromResult(CreateLocation()))
        ], providerTimeout: TimeSpan.FromMilliseconds(100));

        Assert.AreEqual("US", (await composite.GetLocation(IPAddress.Loopback, TestCt)).CountryCode);
        Assert.AreEqual("US", (await composite.GetCurrentLocation(TestCt)).CountryCode);
    }

    // an asset that is not there yet says so as a missing country's file would: only the zip's own list of
    // entries tells a country without ranges
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task A_failed_read_of_a_countrys_ranges_is_not_kept(bool assetNotExists)
    {
        Exception failure = assetNotExists
            ? new NotExistsException("Test: the asset is not there yet.")
            : new IOException("Test: the asset could not be opened.");
        using var provider = new LocalIpRangeLocationProvider(
            new FirstOpenFailsAsset(await BuildRangeZip(), failure), () => null);

        // the first read fails, and gives no ranges; the next one reads again
        Assert.IsFalse((await provider.GetIpRanges("us", TestCt)).Any());
        Assert.IsTrue((await provider.GetIpRanges("us", TestCt)).Any());

        // a country the zip has no file of has no ranges
        Assert.IsFalse((await provider.GetIpRanges("zz", TestCt)).Any());
    }

    [TestMethod]
    public async Task A_canceled_read_of_a_countrys_ranges_is_the_callers_and_is_not_kept()
    {
        using var provider = new LocalIpRangeLocationProvider(
            new FirstOpenFailsAsset(await BuildRangeZip(), failure: null), () => null);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        cts.CancelAfter(100);
        await Assert.ThrowsAsync<OperationCanceledException>(() => provider.GetIpRanges("us", cts.Token));
        Assert.IsTrue((await provider.GetIpRanges("us", TestCt)).Any());
    }

    // The zip is built on one machine and read on another. In Turkish, "IR" in lower case is not "ir", the file's
    // name, and "ir" in upper case is no country code.
    [TestMethod]
    public async Task The_zip_of_ranges_reads_the_same_in_any_culture()
    {
        var zipBytes = await BuildRangeZip();
        var culture = CultureInfo.CurrentCulture;
        try {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            using var provider = new LocalIpRangeLocationProvider(new BytesAsset(zipBytes), () => null);
            Assert.IsTrue((await provider.GetIpRanges("IR", TestCt)).Any());
            CollectionAssert.AreEquivalent(new[] { "IR", "US" }, await provider.GetCountryCodes(TestCt));
            Assert.AreEqual("IR", (await provider.GetLocation(IranAddress, TestCt)).CountryCode);
        }
        finally {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [TestMethod]
    public async Task The_binary_files_country_reads_the_same_in_any_culture()
    {
        using var provider = new LocalIpLocationProvider([
            new LocalIpLocationProvider.IpRangeInfo {
                CountryCode = "ir", IpRange = new IpRange(IPAddress.Parse("5.2.0.0"), IPAddress.Parse("5.2.0.255"))
            }
        ]);

        var culture = CultureInfo.CurrentCulture;
        try {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            Assert.AreEqual("IR", (await provider.GetLocation(IranAddress, TestCt)).CountryCode);
        }
        finally {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [TestMethod]
    public void A_binary_file_cut_short_is_refused()
    {
        using var provider = new LocalIpLocationProvider([
            new LocalIpLocationProvider.IpRangeInfo {
                CountryCode = "ir", IpRange = new IpRange(IPAddress.Parse("5.2.0.0"), IPAddress.Parse("5.2.0.255"))
            }
        ]);
        using var stream = new MemoryStream();
        provider.Serialize(stream);
        var bytes = stream.ToArray();

        // whole, it reads; without its last byte, a country code of one letter is no answer
        using var whole = LocalIpLocationProvider.Deserialize(new MemoryStream(bytes));
        Assert.ThrowsExactly<EndOfStreamException>(() =>
            LocalIpLocationProvider.Deserialize(new MemoryStream(bytes, 0, bytes.Length - 1)));
    }

    // each service's reply, with the region and the city it gives
    [TestMethod]
    [DataRow("ip2location.io", "{\"ip\":\"8.8.8.8\",\"country_code\":\"US\",\"region_name\":\"{0}\",\"city_name\":\"{1}\"}")]
    [DataRow("ipinfo.io", "{\"ip\":\"8.8.8.8\",\"country\":\"US\",\"region\":\"{0}\",\"city\":\"{1}\"}")]
    [DataRow("ipapi.co", "{\"ip\":\"8.8.8.8\",\"country_code\":\"US\",\"region\":\"{0}\",\"city\":\"{1}\"}")]
    public async Task An_online_provider_keeps_the_region_without_a_city_and_the_city_without_a_region(string service,
        string replyFormat)
    {
        // a region with no city
        var location = await GetOnlineLocation(service, replyFormat.Replace("{0}", "California").Replace("{1}", ""));
        Assert.AreEqual("California", location.RegionName);
        Assert.IsNull(location.CityName);

        // a city with no region, which some services send as "NA"
        location = await GetOnlineLocation(service, replyFormat.Replace("{0}", "").Replace("{1}", "Mountain View"));
        Assert.IsNull(location.RegionName);
        Assert.AreEqual("Mountain View", location.CityName);

        location = await GetOnlineLocation(service, replyFormat.Replace("{0}", "na").Replace("{1}", "Mountain View"));
        Assert.IsNull(location.RegionName);
        Assert.AreEqual("Mountain View", location.CityName);
    }

    private async Task<IpLocation> GetOnlineLocation(string service, string reply)
    {
        using var httpClient = new HttpClient(new ReplyHandler(reply));
        using IIpLocationProvider provider = service switch {
            "ip2location.io" => new IpLocationIoProvider(httpClient, "test", apiKey: null),
            "ipinfo.io" => new IpInfoIoProvider(httpClient, "test", apiKey: null),
            "ipapi.co" => new IpApiCoLocationProvider(httpClient, "test"),
            _ => throw new ArgumentOutOfRangeException(nameof(service))
        };

        return await provider.GetLocation(IPAddress.Parse("8.8.8.8"), TestCt);
    }

    // a provider that answers as it is told
    private sealed class TestLocationProvider(Func<CancellationToken, Task<IpLocation>> getLocation) : IIpLocationProvider
    {
        public Task<IpLocation> GetLocation(IPAddress ipAddress, CancellationToken cancellationToken)
        {
            return getLocation(cancellationToken);
        }

        public Task<IpLocation> GetCurrentLocation(CancellationToken cancellationToken)
        {
            return getLocation(cancellationToken);
        }

        public void Dispose()
        {
        }
    }

    private sealed class BytesAsset(byte[] bytes) : IAsset
    {
        public Task<Stream> OpenReadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<Stream>(new MemoryStream(bytes));
        }
    }

    // an asset whose first open fails with the failure, or with none waits for the caller's cancellation; later
    // ones give the bytes
    private sealed class FirstOpenFailsAsset(byte[] bytes, Exception? failure) : IAsset
    {
        private int _openCount;

        public async Task<Stream> OpenReadAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _openCount) != 1)
                return new MemoryStream(bytes);

            if (failure == null)
                await Task.Delay(Timeout.Infinite, cancellationToken);

            throw failure ?? new InvalidOperationException("Test: the wait ended with no cancellation.");
        }
    }

    // answers every request with one JSON reply
    private sealed class ReplyHandler(string reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(reply, Encoding.UTF8, "application/json")
            });
        }
    }
}
