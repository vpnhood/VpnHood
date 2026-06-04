UI
- The UI/front-end is a separate SPA project (VpnHood.Client.WebUI), located at ..\VpnHood.Client.WebUI\ relative to this repo. It consumes the generated TypeScript API stub.

- Never manually edit the TypeScript API stub (the generated .ts file in the Swagger project, e.g. VpnHood.Client.Api.ts). It is auto-generated: build the Swagger project to (re)generate the stub, then the UI consumes the updated stub. run _recreate-api.ps1 to regenerate the API stub and update the UI project reference.

Language
- Use primary constructors when possible
- Use TestHelper.WorkingPath as the temp directory for tests
- Dont use async postfix if the there is no async method with the same name.
- use SafeDisposeAsync if you want to dispose an IAsyncDisposable if you want catch and ignore any exception thrown 
- use AsyncLock instead of SemaphoreSlim for non hot path code, and use it with using statement

Await and ConfigureAwait
- Use .Vhc() instead of .ConfigureAwait(false) if it is available but do not add it to the project if it is not available. 
- For UI code such as android UI, always use .ConfigureAwait(false) when it is required.

Documentation
- The wiki repo (at $(SolutionDir)/../VpnHood.wiki) holds end-user documentation, not development/internal docs. Update it only when I request; keep developer docs inside this repo.

QUIC
- Our QUIC is a custom protocol, not HTTP3. We use it a transport protocol and the protocol is exactly same as HTTP2, so we use it same TCP
