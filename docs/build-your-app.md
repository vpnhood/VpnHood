# Build your VPN app

Start from [VpnHood.App.Template](https://github.com/vpnhood/VpnHood.App.Template): a repository that holds only your
app, for Android, iOS, Windows and Linux. Every part of VpnHood comes into it as a NuGet package, so you neither fork
nor build VpnHood itself, and taking a newer VpnHood means changing one version number.

## From the template to a running app

1. **Create your repository from the template:** choose *Use this template* on its GitHub page, then clone your copy.
2. **Name your app** in `app/app.props`: its id base and name, your company, and its version. The stores and every
   install know your app by the ids built from the base, so choose it before your first release.
3. **Set it up** in `app/appsettings.json`. A key you leave out is off. `DefaultAccessKey` is the access key of the
   server your app connects to; keep it out of a public repository.
4. **Brand it:** your logo and the first-run privacy summary go in `app/branding/`, your icons in `app/icons/`.
5. **Build and run it**, for example `dotnet build src/VpnApp.Windows.Web`. A Debug build without an access key
   connects to VpnHood's sample server, so your first run needs no server of your own.

The template's [README](https://github.com/vpnhood/VpnHood.App.Template#readme) has every setting, what each
platform's build needs, and how to publish.

## Your own servers

Your app reaches your servers through the access key in `DefaultAccessKey`. To run them, see
[the self-hosted VPN docs](https://www.vpnhood.com/self-hosted-vpn/docs/).

## How it fits together

Your app is a head over VpnHood's three layers: the user interface, the app's logic and the VPN engine. Each layer
can be replaced on its own, and the networking packages beneath them work in any .NET application.
[How a VpnHood app is built](client-architecture.md) shows the layers, the contracts between them, and which part to
use for what you are building.
