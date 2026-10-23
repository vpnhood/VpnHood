---
paths:
  - ".github/workflows/**"
  - ".github/scripts/**"
  - "pub/**"
  - "src/Apps/*/*/_publish.ps1"
---

## CI/CD & publishing

- Every app publishes from its own **brand repo**, which holds that app's Fastlane config, store
  metadata and store secrets. This repo holds the code, the single version, and the one shared
  build/store/release definition (`publish_app.yml`) that each brand repo calls.
  **Client** → `vpnhood/Vpnhood.App.Client` (`publish_client.yml`); its GitHub release still lands
  **here**, because that repo sets `VH_PUBLISH_REPO=vpnhood/VpnHood` so existing download links and
  in-app update URLs keep working. **Connect** → `vpnhood/Vpnhood.App.Connect`
  (`connect_publish.yml`), which releases to itself.
  Publish either with its dispatcher **here** — `pub/Client/PublishByGithub.ps1` or
  `pub/Connect/PublishByGithub.ps1`: each prompts for the channel, bumps this repo, waits, then
  dispatches that app's workflow in its brand repo. `bump.yml` never chains an app publish — it
  cannot reach a brand repo, by design.
- The iOS **App Store listing** (metadata + screenshots, no binary) is pushed by a Fastlane `deliver` lane
  (`ios upload_metadata`) via its own workflow — Connect: `publish_appstore_metadata.yml`. The TestFlight
  **build** ships separately through the `*_publish.yml` iOS leg. Connect iOS is TestFlight-only for now.
