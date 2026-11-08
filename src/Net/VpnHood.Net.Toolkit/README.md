# VpnHood.Net.Toolkit

The shared toolkit of [VpnHood](https://github.com/vpnhood/VpnHood)'s networking libraries: IP networks and ranges,
streams, logging with redaction, jobs, assets, timeout collections, JSON converters and assorted helpers. Every other
`VpnHood.Net.*` package builds on it, and it works on its own in any .NET app.

```bash
dotnet add package VpnHood.Net.Toolkit
```

## What it holds

- **Net**: `IpNetwork`, `IpRange` and `IpRangeOrderedList` for address sets and their lookups, `IpEndPointValue`
  and `IPEndPointPairValue` endpoint values, `PortRange`, and address and socket helpers.
- **Streams**: `AsyncStream` and stream decorators, and stream helpers.
- **Logging**: `VhLogger`, the process's one log, whose `Format` methods redact addresses and IDs in anonymous mode;
  console, trace, file and text loggers; `LogService`, which applies the user's log settings.
- **Jobs**: `Job` and `JobRunner`, functions run at an interval on a shared runner.
- **Assets**: `IAssetProvider` and `IAsset`, data files read by path from a folder, a zip, embedded resources or HTTP.
- **Collections**: `TimeoutDictionary`, whose values expire after a time of no use and are disposed.
- **Converters**: JSON converters for addresses, endpoints, versions, time spans, arrays, and enums that fall back on a
  name they do not know.
- **Sockets**: `ISocketFactory`, the seam a platform uses to make its sockets and QUIC clients.
- **ApiClients**, **Trackers**, **Storages** and **Utils**: an HTTP API client base with error mapping, Google
  Analytics 4 trackers, a storage of small text files, and helpers.

## Packages

- `VpnHood.Net.Toolkit` — this toolkit
- `VpnHood.Net.Quic.Abstractions` — the QUIC contract its socket factory names

Requires .NET 10.
