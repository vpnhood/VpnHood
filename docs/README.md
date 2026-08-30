# VpnHood documentation

Everything here is written for a reader with no prior context. Start from the row that matches what
you are trying to do.

| I want to… | Go to |
| --- | --- |
| **Publish my own branded VPN app** to the stores | [publish-your-app/](publish-your-app/README.md) ← start here |
| Know exactly which credential goes where in CI | [cicd/deployment.md](cicd/deployment.md) |
| Understand what free / trial / ad / premium mean in the app | [connection-options.md](connection-options.md) |
| Understand accounts, subscriptions, refunds | [accounts/account-lifecycle.md](accounts/account-lifecycle.md) |
| Work on the iOS app or its VPN extension | [ios/](ios/README.md) |
| Work on split tunnelling (by country, domain, or app) | [split-ip/](split-ip/README.md) |
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
| [connection-options.md](connection-options.md) | What a user is offered per location: free, ad-unlocked, trial, purchase, code. |
| [accounts/account-lifecycle.md](accounts/account-lifecycle.md) | Sign-in, subscriptions, access codes, renewals, refunds — the business flow end to end. |

## Engineering notes

| Area | Document |
| --- | --- |
| iOS app + Network Extension | [ios/](ios/README.md) — architecture, build & provisioning, memory limits, runtime rules |
| Android | [android/google-signin-setup.md](android/google-signin-setup.md) — Google sign-in in debug builds |
| Split tunnelling | [split-ip/](split-ip/README.md) — by country, by domain, by app |

## Releases and CI

| Document | What it covers |
| --- | --- |
| [cicd/deployment.md](cicd/deployment.md) | Secrets, variables, and how a fork configures its own app. |
| [cicd/server-publishing.md](cicd/server-publishing.md) | How the VpnHood server is released. |
| [cicd/white-label-readiness.md](cicd/white-label-readiness.md) | What is still hardcoded per brand — the task list for a no-code white-label builder. |

Release *engineering* runbooks live next to the scripts they describe, not here:
[`pub/RELEASE-STRATEGY.md`](../pub/RELEASE-STRATEGY.md) (versioning and release model),
[`pub/MODULE-REPOS.md`](../pub/MODULE-REPOS.md) and [`pub/TOOL-REPOS.md`](../pub/TOOL-REPOS.md)
(giving a library or tool its own repo and NuGet cadence).

## Legal texts shown to users

[legal/end-user/](legal/end-user/README.md) — binding, user-facing policies. These are published
documents: change them deliberately, and read that folder's README before editing.
