// The .NET runtime, then the app's Main with the page's address: the API and the assets it fetches
// are relative to it. Main returns once the UI is on the page, which is when the notice goes.
import { dotnet } from './_framework/dotnet.js'

const runtime = await dotnet
    .withDiagnosticTracing(false)
    .create();

const config = runtime.getConfig();
await runtime.runMain(config.mainAssemblyName, [globalThis.location.href]);
document.getElementById('loading')?.remove();
