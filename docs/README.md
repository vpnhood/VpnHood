# VpnHood documentation

Everything here is written for a reader with no prior context. Start from the row that matches what
you are trying to do.

| I want to… | Go to |
| --- | --- |
| **Build my own VPN app**, from the template | [build-your-app.md](build-your-app.md) ← start here |
| Publish my own branded VPN app to the stores | [publish-your-app/](publish-your-app/README.md) |
| Know which component talks to which, and in which direction | [topology.md](topology.md) |
| Know how a client is built - a head over three layers: UI, the app's logic, VPN engine - and the two contracts that let you replace any of them; how the app and its VPN service fit together, and which project is which half | [client-architecture.md](client-architecture.md) |
| Find where something lives in `src/`, or what a platform app is made of | [source-layout.md](source-layout.md) |
| Ship a set of files with the app — images, fonts, words, a database | [assets.md](assets.md) |
| Know exactly which credential goes where in CI | [cicd/deployment.md](cicd/deployment.md) |
| Understand what free / trial / ad / premium mean in the app | [connection-options.md](connection-options.md) |
| Understand accounts, subscriptions, refunds | [accounts/account-lifecycle.md](accounts/account-lifecycle.md) |
| Install, run or script the Linux client (desktop or headless) | [linux/](linux/README.md) |
| Work on the iOS app or its VPN extension | [ios/](ios/README.md) |
| Work on split tunnelling (by country, domain, or app) | [split-ip/](split-ip/README.md) |
| Know what the apps and the server report to analytics, and what turns it off | [tracking/](tracking/README.md) |
| Read the policies our users are shown | [legal/end-user/](legal/end-user/README.md) |

---

## Publishing your own app

For anyone standing up their own app — a fork, a white-label, or us shipping a new brand.

| Document | What it covers |
| --- | --- |
| [publish-your-app/README.md](publish-your-app/README.md) | **The map.** What to prepare, what order, what only a human can do. Non-technical. |
| [publish-your-app/apple-app-store.md](publish-your-app/apple-app-store.md) | Every hand step in Apple's two consoles. |
| [publish-your-app/google-play.md](publish-your-app/google-play.md) | Every hand step in the Play Console. |
| [cicd/deployment.md](cicd/deployment.md) | The credential-by-credential reference the pages above defer to. |

Store policy and legal checkpoints a publisher must clear:

| Document | What it covers |
| --- | --- |
| [legal/developer/APP_STORE_PRIVACY.md](legal/developer/APP_STORE_PRIVACY.md) | Apple's privacy questionnaire, answered and explained — plus the non-privacy rules that remove VPN apps (who may publish one, age rating, trademark, licence). |
| [legal/developer/APP_STORE_TERRITORIES.md](legal/developer/APP_STORE_TERRITORIES.md) | Which countries to switch off before the first release, and why. |
| [legal/developer/APP_STORE_EXPORT_COMPLIANCE.md](legal/developer/APP_STORE_EXPORT_COMPLIANCE.md) | The encryption declaration Apple requires, and the one setting that must never be flipped. |
| [legal/developer/EXPORT_COMPLIANCE.md](legal/developer/EXPORT_COMPLIANCE.md) | The underlying export classification, stated publicly. |

## How the product behaves

| Document | What it covers |
| --- | --- |
| [connection-options.md](connection-options.md) | What a user is offered per location: free, ad-unlocked, trial, purchase, code; and where an app shows premium, or never does. |
| [accounts/account-lifecycle.md](accounts/account-lifecycle.md) | Sign-in, subscriptions, access codes, renewals, refunds — the business flow end to end. |

## Engineering notes

| Area | Document |
| --- | --- |
| Source layout | [source-layout.md](source-layout.md) — the five layers under `src/`, what a head is made of, what every Android app shares and how a head overrides it, how the browser page is built and placed |
| Assets and zips | [assets.md](assets.md) — why data ships as a placed zip and never as an embedded resource, the provider chain that reads one, and why the paired-phone page is WebAssembly |
| Component topology | [topology.md](topology.md) — who connects to whom, in which direction, and how many of each |
| Client architecture | [client-architecture.md](client-architecture.md) — the head, the app you publish, over three layers (AppUi, AppLib, Core) and their two contracts, the app API and the VPN service contract, with what each lets you build on your own; the app and its VPN service: where each runs on every platform, the channel between them, and the projects of each half |
| Protocol 15 | [protocol-15.md](protocol-15.md) — HTTP request proof, rollout order, compatibility and proxy limitations |
| IP-blocking release checks | [ip-blocking-release.md](ip-blocking-release.md) — integration suites, package checks, deployment/device evidence and the staged rollout |
| iOS app + Network Extension | [ios/](ios/README.md) — architecture, build & provisioning, memory limits, runtime rules |
| Android | [android/google-signin-setup.md](android/google-signin-setup.md) — Google sign-in in debug builds |
| Split tunnelling | [split-ip/](split-ip/README.md) — by country, by domain, by app |
| Tracking | [tracking/](tracking/README.md) — what each component reports to Google Analytics, to which id, and the switch |

## Releases and CI

| Document | What it covers |
| --- | --- |
| [cicd/deployment.md](cicd/deployment.md) | Secrets, variables, and how a fork configures its own app. |
| [cicd/server-publishing.md](cicd/server-publishing.md) | How the VpnHood server is released. |

Release *engineering* runbooks live next to the scripts they describe, not here:
[`pub/RELEASE-STRATEGY.md`](../pub/RELEASE-STRATEGY.md) (versioning and release model),
[`pub/MODULE-REPOS.md`](../pub/MODULE-REPOS.md) and [`pub/TOOL-REPOS.md`](../pub/TOOL-REPOS.md)
(giving a library or tool its own repo and NuGet cadence).

## Legal texts shown to users

[legal/end-user/](legal/end-user/README.md) — binding, user-facing policies. These are published
documents: change them deliberately, and read that folder's README before editing.
