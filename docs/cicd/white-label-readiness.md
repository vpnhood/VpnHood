# White-label readiness — what still needs a file edit

Written for the planned **white-label builder service** (a merchant supplies a name, a logo and
colours; we produce their app and upload it into their own store accounts) and the planned
**starter repo** (a small project that
consumes our NuGet packages instead of forking the monorepo).

Today a fork can be configured almost entirely from GitHub **variables and secrets** — see
[deployment.md](deployment.md). This page lists what that configuration does **not** yet reach:
every value a new brand must still change by editing a file. Each one is a task for the builder
service, because a merchant cannot be asked to edit source.

Verified against the tree on 2026-08-29 (items 1-5 and 8 on 2026-09-24); re-check before planning
work.

## Already configurable (no code edit)

`publish.json` via the `PUBLISH` variable — release repo, artifact title, Windows install page,
and the Android application id per distribution. `appsettings.json` via the `APPSETTINGS` variable —
the product's settings (`AppConfigs` and each product's own keys): `PortalBaseUri`,
`RemoteSettingsUrl`, `GoogleSignInClientId`, the `PrivacyPolicyUrl` / `TermsOfUseUrl` the app links
to, the analytics id, and the ad **unit** ids. The embedded default server key via `ACCESS_KEY_AD` /
`ACCESS_KEY_PREMIUM`.

## Written once, in the app's identity

One file per app, `src/Apps/<Product>/Directory.Build.props`, names its id base and its name
(`VhAppIdBase`, `VhAppName`, and the package title when it must not follow the name). Every head builds
its ids and names from them at build time - the Android package, the iOS bundles and App Group, the
Windows and Linux app ids, the desktop executable and service, and the constants the code and
Android's attributes read - so a builder writes that one file, and no head names the app. See
[source-layout](../source-layout.md#the-apps-identity).

## Still hardcoded

| # | Value | Where | Why it blocks a no-code flow | Suggested shape |
| --- | --- | --- | --- | --- |
| 1 | **App display name** | *Done (2026-09-24):* the app's identity (`VhAppName`), above | — | — |
| 2 | **UI theme** (`UiTheme`) | Hardcoded once per product: `Connect/Connect/ConnectAppOptions.cs` (Client keeps the default) | Selects the colour theme; the product's options builder names it beside the logo and the consent summary (`LogoAssetPath` / `PrivacyConsentAssetName`) | Read it from the app's identity or its settings |
| 3 | **The UI only knows two looks** | `VpnHood.AppUi.Spa` (the SPA, a sample now; the Avalonia UI syncs its themes from it) — themes `blue` / `violet` in `src/theme/themes.ts`, `public/branding/<theme>/` | A third colour set needs a UI release per merchant. The logo and the consent summary no longer do: the product's options builder names them (`LogoAssetPath`, `PrivacyConsentAssetName`) and a fork supplies the files in a zip of its own, named before the store in `AppOptions.UiZipAssets`, with its own names reaching the text as `{appName}` / `{companyName}` | Make the colours data-driven: a theme supplied as assets rather than built in |
| 4 | **iOS bundle ids** (host + extension) and App Group | *Done (2026-09-24):* built from the app's identity (`VhAppIdBase`), above | — | — |
| 5 | **AdMob application id** | `src/Apps/Connect/Connect.Android.Google/App.cs` — the `[MetaData]` attribute's value | An attribute takes a constant only; the ad *unit* ids are already overridable | A property of the app's identity, which the constants carry as they carry the name (not yet decided) |
| 6 | **Upstream code repo** | `env.CODE_REPO: vpnhood/VpnHood` in `.github/workflows/publish_app.yml` and each `_build_app_*.yml` | No variable override (the server pipeline has one, this does not), so a fork that renames or relocates the monorepo must edit workflow files | Read a `CODE_REPO` variable with the current value as default |
| 7 | **Store ids in the brand repo** | `fastlane/Appfile` (`package_name`), and the iOS `app_identifier` in that repo's `publish_listing.yml` stub | fastlane reads files, not our variables | Template these from the same variables the builds use |
| 8 | **Each head's update feed** | every head's `App.cs` (`UpdateInfoUrl`) | It was an `appsettings.json` key; a head now states it in code. (The desktop app ids this row also named come from the app's identity since 2026-09-24) | Build it from `publish.json`'s `RepoUrl` and `PackageTitle`, which already name the release it points at |

## Notes for the builder service

- Items 2, 5 and 8 are the same root cause: **values in the code of a head or a product**, one of
  them in a C# attribute. The app's ids and names had it too, and now come from one file (above);
  the same move unblocks most of what is left.
- Item 3 is the largest piece of work and the one a merchant notices most, since it is their logo
  and their colours.
- A **starter repo** consuming NuGet packages sidesteps 6 and 7 by construction (it owns its own
  workflows and store files), and 1 and 4 too: its heads get the identity's build targets through
  `VpnHood.AppLib.App`, so they build from the starter repo's own identity file. All but the iOS
  network extension, which references no AppLib package and must be given those targets another way
  (not yet decided). It inherits 2, 3 and 5 unless those move into configuration first.
- Anything the stores require of the publisher — accounts, agreements, banking, questionnaires,
  submission — stays manual no matter how good the tooling gets. The merchant-facing walkthrough of
  exactly those steps is [publish-your-app](../publish-your-app/README.md); a managed service
  performs them with delegated access, it does not remove them.
