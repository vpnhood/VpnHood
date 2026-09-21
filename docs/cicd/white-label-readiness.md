# White-label readiness — what still needs a file edit

Written for the planned **white-label builder service** (a merchant supplies a name, a logo and
colours; we produce and publish their app) and the planned **starter repo** (a small project that
consumes our NuGet packages instead of forking the monorepo).

Today a fork can be configured almost entirely from GitHub **variables and secrets** — see
[deployment.md](deployment.md). This page lists what that configuration does **not** yet reach:
every value a new brand must still change by editing a file. Each one is a task for the builder
service, because a merchant cannot be asked to edit source.

Verified against the tree on 2026-08-29; re-check before planning work.

## Already configurable (no code edit)

`publish.json` via the `PUBLISH` variable — release repo, artifact title, Windows install page,
and the Android application id per distribution. `appsettings.json` via the `APPSETTINGS` variable —
including `AppId`, `PortalBaseUri`, `UpdateInfoUrl`, `RemoteSettingsUrl`, `GoogleSignInClientId`,
the `PrivacyPolicyUrl` / `TermsOfUseUrl` the app links to,
and the ad **unit** ids. The embedded default server key via `ACCESS_KEY_AD` / `ACCESS_KEY_PREMIUM`.

## Still hardcoded

| # | Value | Where | Why it blocks a no-code flow | Suggested shape |
| --- | --- | --- | --- | --- |
| 1 | **App display name** | `src/Apps/Connect/Connect.Android.Google/AppConfigs.cs` — `public const string AppName` | It is a compile-time `const` consumed by the `[Application(Label=…)]` attribute, so it cannot come from `appsettings.json` like its neighbours | Generate the attribute value at build time from a build property, or move the label out of the attribute |
| 2 | **UI theme** (`UiTheme`) | Hardcoded per app entrypoint: `Connect.Android.Google/App.cs`, `Connect.Android.Web/App.cs`, `Connect.Win.Web/App.cs`, `Connect.Linux.Web/App.cs`, `Connect.Ios.Apple/AppDelegate.cs` | Selects the colour theme (the logo and the consent summary are `LogoAssetPath` / `PrivacyConsentAssetName`, already read from `appsettings.json`) | Read from `appsettings.json` alongside `AppId` |
| 3 | **The UI only knows two looks** | `VpnHood.AppUi.Spa` (the SPA, a sample now; the Avalonia UI syncs its themes from it) — themes `blue` / `violet` in `src/theme/themes.ts`, `public/branding/<theme>/` | A third colour set needs a UI release per merchant. The logo and the consent summary no longer do: a head names them (`LogoAssetPath`, `PrivacyConsentAssetName`) and a fork supplies the files in a zip of its own, named before the store in `AppOptions.UiZipAssets`, with its own names reaching the text as `{appName}` / `{companyName}` | Make the colours data-driven: a theme supplied as assets rather than built in |
| 4 | **iOS bundle ids** (host + extension) | `src/Apps/Connect/Connect.Ios.Apple/*.csproj`, `Connect.Ios.Extension/*.csproj` — `<BundleIdentifier>` | `publish.json`'s distribution block is Android-only, so iOS ids cannot be configured at all | Add an iOS block to `publish.json` and pass `/p:BundleIdentifier` |
| 5 | **AdMob application id** | `src/Apps/Connect/Connect.Android.Google/AppConfigs.cs` — `const AdMobApplicationId`, used in a `[MetaData]` attribute | Same `const`-in-attribute problem as the app name; the ad *unit* ids are already overridable | Same fix as #1 |
| 6 | **Upstream code repo** | `env.CODE_REPO: vpnhood/VpnHood` in `.github/workflows/publish_app.yml` and each `_build_app_*.yml` | No variable override (the server pipeline has one, this does not), so a fork that renames or relocates the monorepo must edit workflow files | Read a `CODE_REPO` variable with the current value as default |
| 7 | **Store ids in the brand repo** | `fastlane/Appfile` (`package_name`), and the iOS `app_identifier` in that repo's `publish_listing.yml` stub | fastlane reads files, not our variables | Template these from the same variables the builds use |

## Notes for the builder service

- Items 1, 2 and 5 are all the same root cause: **values baked into C# attributes**. Fixing that one
  pattern unblocks most of a no-code flow.
- Item 3 is the largest piece of work and the one a merchant notices most, since it is their logo
  and their colours.
- A **starter repo** consuming NuGet packages sidesteps 6 and 7 by construction (it owns its own
  workflows and store files), but inherits 1–5 unless those move into configuration first.
- Anything the stores require of the publisher — accounts, agreements, banking, questionnaires,
  submission — stays manual no matter how good the tooling gets. The merchant-facing walkthrough of
  exactly those steps is [publish-your-app](../publish-your-app/README.md); a managed service
  performs them with delegated access, it does not remove them.
