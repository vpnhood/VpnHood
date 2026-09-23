// The .NET runtime, then the app's Main with the page's address: the API and the assets it fetches
// are relative to it. Main returns once the UI is on the page, which is when the notice goes.
import { dotnet } from './_framework/dotnet.js'

// Whatever stops the page stops it here, and the notice is the only place a person can be told:
// nothing of this runs in the app, so the app's log holds none of it. A page that failed says so
// rather than waiting forever - the app's web view shows this page, and a spinner that never ends
// reads as a hung app.
try {
    const runtime = await dotnet
        .withDiagnosticTracing(false)
        .create();

    const config = runtime.getConfig();
    await runtime.runMain(config.mainAssemblyName, [globalThis.location.href]);
    document.getElementById('loading')?.remove();
}
catch (err) {
    console.error(err);
    const notice = document.getElementById('loading');
    if (notice) {
        notice.classList.add('failed');
        notice.textContent = `${err?.stack ?? err}`;
    }
}
