# VpnHood — Claude instructions

The shared coding conventions and working agreements for this repo. They are the source of
truth — follow them, and when a new durable convention is agreed, update this file.

## UI
- The UI/front-end is a separate SPA project (VpnHood.Client.WebUI), located at `..\VpnHood.Client.WebUI\`
  relative to this repo. It consumes the generated TypeScript API stub.
- Never manually edit the TypeScript API stub (the generated .ts file in the Swagger project, e.g.
  VpnHood.Client.Api.ts). It is auto-generated: build the Swagger project to (re)generate the stub, then
  the UI consumes the updated stub. Run `_recreate-api.ps1` to regenerate the API stub and update the UI
  project reference.

## Language
- Use primary constructors when possible.
- Use `TestHelper.WorkingPath` as the temp directory for tests.
- Don't use the `async` postfix if there is no async method with the same name.
- Use `SafeDisposeAsync` if you want to dispose an `IAsyncDisposable` and catch/ignore any exception thrown.
- Use `AsyncLock` instead of `SemaphoreSlim` for non-hot-path code, and use it with a `using` statement.

## Await and ConfigureAwait
- Use `.Vhc()` instead of `.ConfigureAwait(false)` if it is available, but do not add it to the project if
  it is not available.
- For UI code such as Android UI, always use `.ConfigureAwait(false)` when it is required.

## Documentation
- The wiki repo is a separate repository located at `..\VpnHood.wiki` relative to this repo (i.e.
  `$(SolutionDir)/../VpnHood.wiki`). It holds end-user documentation, not development/internal docs. Update
  it only when I request; keep developer docs inside this repo.

## QUIC
- Our QUIC is a custom protocol, not HTTP3. We use it as a transport protocol and the protocol is exactly
  the same as HTTP2, so we treat it the same as TCP.

## iOS (Client & Connect apps)
- The iOS apps live in `Src/Apps/{Client,Connect}.Ios` (host) + `…{Client,Connect}.Ios.Extension` (Network
  Extension `.appex`); the real device/extension/TUN/TCP-stack code is in `Src/Core/*` (`Devices.Ios`,
  `VpnAdapters.IosTun`, `TcpStack`, `Quic.Ios`). The extension projects are one-file `[Register]` shims.
- **Read [`docs/ios/`](docs/ios/) before working on anything iOS** — especially
  `ios-extension-memory-and-throughput.md` before touching memory/throughput/TCP-stack code (the extension
  runs under a ~52 MB jetsam limit).
- Build **Release** for device with `~/.dotnet11/dotnet` (TFM `net11.0-ios` / CoreCLR — the system `dotnet`
  can't target it). Don't commit a test `AccessKey` in `AppConfigs.cs` (production defaults to `null`).
