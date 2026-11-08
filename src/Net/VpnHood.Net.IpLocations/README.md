# VpnHood.Net.IpLocations

IP location for .NET: find the country, and where known the region and city, of an IP address or of the device's
own public address, from online services or from a local database. It is the location layer of
[VpnHood](https://github.com/vpnhood/VpnHood): the app finds its country and the IP ranges of the countries it
splits the tunnel by, and a server finds its own country. It works for any app that needs to place an address.

```bash
dotnet add package VpnHood.Net.IpLocations
```

## How it works

- `IIpLocationProvider` finds where an address is (`GetLocation`) and where the device is
  (`GetCurrentLocation`); `ICurrentIpLocationProvider` only the latter. `IIpRangeLocationProvider` also gives each
  country's IP ranges, from a local database.
- Online providers: `IpApiCoLocationProvider`, `IpInfoIoProvider`, `IpLocationIoProvider`, and
  `CloudflareLocationProvider` for the current location's country. They take your `HttpClient` and User-Agent.
- Local providers give the country only: `LocalIpLocationProvider` holds ranges in memory;
  `LocalIpRangeLocationProvider` reads a zip of country ranges, a country at a time. `Ip2LocationDbParser` builds
  both from IP2Location's LITE DB1 database. `VpnHood.Net.IpLocations.Providers.SqliteProvider` keeps the ranges
  in SQLite instead.
- `CompositeIpLocationProvider` and `CompositeCurrentIpLocationProvider` try several providers in order. Each gets a
  token canceled after its timeout (for the current location 5 seconds by default; for an address none), so the
  timeout holds for a provider that observes it. Failures are logged, except `NotSupportedException`.
- A local lookup of the current location still finds the device's public address online first.

## Quick start

The examples below run as a test in CI
([IpLocationsReadmeExampleTest.cs](https://github.com/vpnhood/VpnHood/blob/develop/tests/VpnHood.Test/Tests/IpLocationsReadmeExampleTest.cs)).
Their namespaces are `System.Net`, `VpnHood.Net.IpLocations.Providers.Offlines` and `VpnHood.Net.Toolkit.Net`.

### Look up an address locally

```csharp Snippet:IpLocationsLocal
// IP ranges with their countries: two here; in an app, IP2Location's database (Ip2LocationDbParser)
using var provider = new LocalIpLocationProvider([
    new LocalIpLocationProvider.IpRangeInfo {
        IpRanges = new IpRange(IPAddress.Parse("1.0.0.0"), IPAddress.Parse("1.0.0.255")),
        CountryCode = "AU"
    },
    new LocalIpLocationProvider.IpRangeInfo {
        IpRanges = new IpRange(IPAddress.Parse("5.0.0.0"), IPAddress.Parse("5.0.0.255")),
        CountryCode = "DE"
    }
]);

var location = await provider.GetLocation(IPAddress.Parse("5.0.0.10"), CancellationToken.None);
Console.WriteLine($"{location.CountryName} ({location.CountryCode})"); // Germany (DE)
```

### Save and load the ranges

```csharp Snippet:IpLocationsSaveLoad
var path = Path.GetTempFileName(); // in an app, a file in its data folder

// save the ranges once, in their compact binary form...
await using (var file = File.Create(path))
    provider.Serialize(file);

// ...and load them when the app starts
using var loaded = LocalIpLocationProvider.Deserialize(File.OpenRead(path));
```

## Packages

- `VpnHood.Net.IpLocations` — the providers
- `VpnHood.Net.IpLocations.Providers.SqliteProvider` — the SQLite database and its provider

Requires .NET 11.
