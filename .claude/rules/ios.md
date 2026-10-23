---
paths:
  - "src/Apps/*/*.Ios.*/**"
  - "src/**/*.Ios/**"
  - "src/Net/VpnHood.Net.VpnAdapters.IosTun/**"
  - "**/*TcpStack*/**"
  - "**/buildTransitive/ios/**"
  - "docs/ios/**"
---

## iOS (Client & Connect apps)

- The iOS apps live in `src/Apps/{Client,Connect}/{Client,Connect}.Ios.Apple` (host) + `….Ios.Extension` (Network
  Extension `.appex`); the real device/extension/TUN/TCP-stack code is in `src/Core/VpnHood.Core.Client.Devices.Ios`
  and `src/Net/*` (`VpnAdapters.IosTun`, `TcpStack`, `Quic.Ios`). The extension projects are one-file `[Register]` shims.
- **Read [`docs/ios/`](docs/ios/) before working on anything iOS** — especially
  `ios-extension-memory-and-throughput.md` before touching memory/throughput/TCP-stack code (the extension
  runs under a ~52 MB jetsam limit).
- Build **Release** for device with `~/.dotnet11/dotnet` (TFM `net11.0-ios` / CoreCLR — the system `dotnet`
  can't target it). Don't commit a test access key in a head (production has none in code).
- When asked to build/run/launch an app without naming the product, use the **Client** app
  (`src/Apps/Client/Client.Ios.Apple`), not Connect.
- Updates come from the **App Store only**: wire `AppStoreAppUpdaterProvider` and name **no
  `UpdateInfoUrl`** in the iOS heads' updater options. That feed describes downloadable packages, so a
  non-null value makes the UI offer a direct download — wrong channel on iOS, and an App Review problem.
