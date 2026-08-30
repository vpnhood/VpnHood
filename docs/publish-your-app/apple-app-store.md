# Apple App Store — the steps only you can do

Everything on this page happens in a browser, by hand, in **your** Apple accounts. Nothing here can
be automated away: Apple has no interface for most of it, and the parts that do have one still
require you to have accepted the agreements first.

Work through it in order. Come back to [start here](README.md) when you are done.

> **Current limitation.** For a CONNECT-style app the pipeline uploads builds to **TestFlight**,
> Apple's testing channel — not to the public App Store. The listing, the subscriptions and the
> screenshots all publish normally; only the final binary promotion is held back deliberately.
> Everything on this page is still required, and is what makes that promotion possible.

Two websites are involved, and it matters which one you are on:

- **Apple Developer** (developer.apple.com) — identities, capabilities, certificates. The
  "plumbing".
- **App Store Connect** (appstoreconnect.apple.com) — the app itself, its listing, its
  subscriptions, its review. The "shopfront".

---

## 1. Enrol as an organization

A VPN app is only accepted from an **organization** account belonging to the entity that provides
the VPN service — not a personal account. Enrolment needs your company's legal details and usually
a **D‑U‑N‑S number** (free, but obtaining one can take up to a couple of weeks).

Then, in App Store Connect → **Business**: accept the current agreements. If you intend to sell
anything, complete the **Paid Applications** agreement, plus your **bank** and **tax** details.
Until those are complete and *active*, every product you create is unsellable — the console lets
you configure it all and then quietly refuses to sell.

---

## 2. Choose your identifiers

Your app needs **two** identities, because the VPN itself runs in a separate piece of the app:

| Piece | Looks like |
| --- | --- |
| The app | `com.yourcompany.yourapp` |
| The VPN part ("network extension") | `com.yourcompany.yourapp.networkextension` |

Pick them carefully — **they can never be changed** once used, and they must match what the
pipeline is configured to build.

In Apple Developer → **Identifiers**:

1. Create an **App Group** — a shared box the two pieces use to talk to each other. Name it after
   your app, e.g. `group.com.yourcompany.yourapp`.
2. Create the **app** identifier. Switch on: **App Groups** (select the group above), **Network
   Extensions**, and — only if you will sell subscriptions — **In-App Purchase** and **Sign in with
   Apple**.
3. Create the **network extension** identifier. Switch on **App Groups** (same group) and **Network
   Extensions**.

> **The rule that catches everyone:** changing a capability later **invalidates every signing
> profile** on that identifier. Apple never repairs them; you must regenerate the profiles and hand
> them to the pipeline again. So decide about selling *now*, not after the first release.

---

## 3. Create the signing material

Still in Apple Developer:

1. **Certificates → Apple Distribution.** One certificate signs all your apps. Export it from
   Keychain Access as a `.p12` file **with its private key** and give it a password you keep.
2. **Profiles → App Store**, twice — once for the app identifier, once for the network extension
   identifier. Download both files.

These three files plus the password are what let the pipeline sign your app. Where to paste them:
[deployment](../cicd/deployment.md).

---

## 4. Create the app record

In App Store Connect → **Apps → +**:

- Pick your app identifier, your app's public **name** (must be unique across the whole store), the
  primary language, and an SKU (any internal code, e.g. your bundle id).
- Under **Pricing and Availability → Availability**, **deselect the territories a VPN must not be
  sold in** before you ever submit: [which ones and why](../legal/developer/APP_STORE_TERRITORIES.md).

The app must exist here before the pipeline's first upload — uploading is the only part of this
section that is automated.

---

## 5. Create the keys the automation uses

Two different keys, for two different jobs. Both are created once and shown once — save them
immediately.

| Key | Created in | Used for |
| --- | --- | --- |
| **App Store Connect API key** (App Manager role) | Users and Access → Integrations → App Store Connect API | Uploading builds, managing the listing |
| **In-App Purchase key** *(only if you sell)* | Users and Access → Integrations → In-App Purchase | Letting your billing portal verify purchases |

Use the **In-App Purchase** key for your portal rather than a full API key: it can only check
purchases, so if your server is ever compromised, nobody can touch your certificates or your apps
with it.

---

## 6. Subscriptions

Skip this section entirely if your first release is free.

In App Store Connect → your app → **Subscriptions**: create one subscription group, then one
product per plan (for example monthly and yearly). Each product needs **all** of:

- a **product id** that exactly matches what your billing portal expects — the app carries no
  fallback, so a mismatch means the plan cannot be sold;
- a **price**;
- at least one **localization** (display name and description);
- an **App Review screenshot** — a picture of your app's purchase screen;
- **availability** — the territories it is sold in.

Miss any one and the product sits in *missing metadata* and cannot be sold, with no explanation of
which piece is absent. Our audit tool lists exactly what is missing for every product, and the
screenshot can be generated for you rather than photographed by hand — see
[the in-app purchase checklist](../ios/build-deploy-and-provisioning.md#enable-in-app-purchase-connect-style-apps-white-labelfork-checklist).

Also set, on the same page: **App Store Server Notifications** (both the sandbox and production
URLs) pointing at your billing portal, so renewals, cancellations and refunds reach you.

Finally, create a **sandbox tester** account (Users and Access → Sandbox) and sign into it on a
test device under *Settings → Developer → Sandbox Apple Account*. Sandbox purchases are free and
run on a compressed clock — a "monthly" plan renews every few minutes and then expires for good, so
test quickly and expect to buy again.

---

## 7. The paperwork that gates review

Fill these in App Store Connect before submitting. All are one-time, all are mandatory, and each
one blocks the submission until answered:

1. **App Privacy** — what your app collects. Do not copy anyone else's answers; they describe
   someone else's servers. Guidance and the reasoning behind each answer:
   [App Store privacy](../legal/developer/APP_STORE_PRIVACY.md).
2. **Age rating** — the honest answers give a VPN a **17+** rating.
3. **Export compliance** — routine, but there is one setting that must never be flipped, or every
   upload is rejected: [export compliance](../legal/developer/APP_STORE_EXPORT_COMPLIANCE.md).
4. **Trader status** — required for the EU. If you sell, you are a trader, and your company address
   and contact details appear publicly on your listing.
5. **Privacy policy URL** — a working page on your own domain, under your own name.

---

## 8. First release quirks

Things that only bite on the very first version:

- **No "What's New" text.** Apple rejects a first version that carries release notes. Your later
  releases add them automatically.
- **The listing must be published in two passes** — screenshots first, then text (a long-standing
  bug in Apple's own tooling aborts the combined push).
- **iPad screenshots are mandatory** if your app supports iPad, which it does by default.
- **The listing locks while a version is in review.** Publishing your listing at that moment is
  skipped with a warning rather than failing; it goes through once review ends.
- **Attach your subscriptions to the first version** when you submit it. A brand-new subscription
  is reviewed together with the app version, not on its own.

---

## 9. Submit

Add a **review note** with a working test account and anything a reviewer needs to see the app
working — reviewers must be able to use every feature, including anything behind a purchase.

Then submit, and wait. If you are rejected, read the message literally: first rejections are
usually about the paperwork above, not about your app.
