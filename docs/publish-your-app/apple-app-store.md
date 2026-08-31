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
- at least one **localization** (display name and description) — but see below, one is rarely what
  you want;
- an **App Review screenshot** — a picture of your app's purchase screen;
- **availability** — the territories it is sold in. This is a *separate* list from the app's, and it
  defaults to everywhere: [set it to match](../legal/developer/APP_STORE_TERRITORIES.md#your-subscriptions-have-their-own-list).

Miss any one and the product sits in *missing metadata* and cannot be sold, with no explanation of
which piece is absent. Our audit tool lists exactly what is missing for every product, and the
screenshot can be generated for you rather than photographed by hand — see
[the in-app purchase checklist](../ios/build-deploy-and-provisioning.md#enable-in-app-purchase-connect-style-apps-white-labelfork-checklist).

**Translate the subscription texts, and do it before you submit.** Your in-app paywall is translated
with the rest of the app, but the **native purchase sheet** — the system panel where the money is
actually confirmed — shows *your* subscription display name and description, and Apple localizes
only its own wording around them. With one English localization, that screen reads in English to
every customer in the world.

Do it before the first submission rather than after, because subscription texts go through App
Review: bundled into the version's submission they cost nothing, added later they are a separate
review round for the products. Write the English strings once in
`store-i18n/en-US/subscriptions.json`, let the translator fill the other locales like every other
store text, and push them with:

```bash
node e2e/store-subscriptions.mjs --bundle-id <your.bundle.id> --root <your store repo> --keys-dir <your keys>
```

It refuses over-length text rather than truncating it (Apple allows 30 characters for a display name
and 45 for a description — short enough that some languages need a rewrite, not a translation), and
`--check` shows what would change without writing.

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

1. **Pricing** — even a **free** app needs a price explicitly set. Until you press *Add Pricing* on
   Pricing and Availability and choose Free, the version cannot enter review, and Apple's error
   says only "this resource cannot be reviewed" without naming pricing. A base territory alone is
   not enough; the price itself must exist.

   For an app of this shape the answer is always the same: **price Free, base territory USA,
   starting immediately with no end date, available everywhere except the territories you
   deliberately switch off** ([which ones, and why](../legal/developer/APP_STORE_TERRITORIES.md)).
   The price schedule offers start and end dates because it is built for temporary sales; a
   permanently free app wants neither. The app is the free
   container; the **subscriptions** carry the money and are priced separately under *Subscriptions*,
   which is why a paid-feature app is still "Free" here. Charging for the app itself instead would
   make every existing user pay again at the next release, and is not how a subscription VPN is
   sold.
2. **App Privacy** — what your app collects. Do not copy anyone else's answers; they describe
   someone else's servers. **Saving is not enough:** the panel has a separate **Publish** button,
   and until you press it the version is blocked in the same silent way as pricing. Guidance and the
   reasoning behind each answer: [App Store privacy](../legal/developer/APP_STORE_PRIVACY.md).
3. **Primary category** — App Information → *Primary Category*. Easy to miss because the field is
   blank rather than flagged, and it blocks review as silently as the two above. A VPN belongs in
   **Utilities**. A secondary category is optional and can stay empty.
4. **Age rating** — the honest answers give a VPN a **17+** rating.
5. **Export compliance** — routine, but there is one setting that must never be flipped, or every
   upload is rejected: [export compliance](../legal/developer/APP_STORE_EXPORT_COMPLIANCE.md).
6. **Trader status** — required for the EU. If you sell, you are a trader, and your company address
   and contact details appear publicly on your listing.
7. **Privacy policy URL** — a working page on your own domain, under your own name.
8. **Licence agreement** — App Information → *License Agreement*. Leave it alone and Apple's
   [standard EULA](https://www.apple.com/legal/internet-services/itunes/dev/stdeula/) governs every
   sale, which is the right answer until you have terms of your own that a lawyer has read. Whichever
   you use, the app has to link to the same one — see below.

**Pricing, App Privacy and the primary category** waste the most time, because none of them
announces itself: the version simply refuses to enter review, and the only error — in the console
and over the API alike — is *"this resource cannot be reviewed, please check associated errors"*,
which names nothing. If a submission is blocked with nothing obviously missing, check those three
first, in that order. All three bit this project's own first submission.

### Both documents have to be reachable from inside the app

A reviewer will not go looking on your store listing. App Review **3.1.2** wants the **Terms of Use
(EULA)** and the **Privacy Policy** openable from the purchase screen itself, at the moment money is
asked for. This project's own CONNECT app was rejected on that point with a listing that already
carried both links, so treat the two fields above as necessary and not sufficient.

Both addresses are settings, not code — `PrivacyPolicyUrl` and `TermsOfUseUrl` in your app settings
([deployment](../cicd/deployment.md)). Leave one empty and the app simply shows no link for it,
which for the Privacy Policy is a rejection. The App Store build is the one exception: it always
links Apple's standard EULA whatever `TermsOfUseUrl` says, because that is the agreement a buyer
there actually accepts for as long as no custom EULA is registered in App Information above.

---

## 8. First release quirks

Things that only bite on the very first version:

- **Check the version number on the record, not just in the build.** App Store Connect pre-fills
  `1.0` when you create an app, and that string is what customers see on the product page. It is a
  *separate field* from your binary's `CFBundleShortVersionString`, nothing keeps the two in step,
  and Apple does not complain: a build declaring `8.1.847` attaches to a record saying `1.0`
  silently. Leave them apart and the store advertises one number while the app reports another in
  Settings, in its about screen, and in every crash log and support ticket. Set the record to the
  version your build actually carries before you submit. Nothing in the pipeline does it for you —
  the listing publish writes text and screenshots into whichever version is open, and never touches
  the number.
- **No "What's New" text.** Apple rejects a first version that carries release notes. Your later
  releases add them automatically.
- **The listing must be published in two passes** — screenshots first, then text (a long-standing
  bug in Apple's own tooling aborts the combined push).
- **iPad screenshots are mandatory** if your app supports iPad, which it does by default.
- **The listing locks while a version is in review.** Publishing your listing at that moment is
  skipped with a warning rather than failing; it goes through once review ends.
- **Attach your subscriptions to the first version** when you submit it. A brand-new subscription
  is reviewed together with the app version, not on its own.
- **Decide about Apple Silicon Macs — and use the right control.** Two things in App Store Connect
  sound the same and are not. The one you want is the **"Make this app available on Mac"**
  checkbox: it offers your existing iOS binary to Apple Silicon Macs, reusing your iPad
  screenshots, with no separate version and no separate review. The one to avoid is **adding a
  macOS platform** to the app record — that creates a second, independent `MAC_OS` version that
  demands its own macOS *binary* (Catalyst or native) and its own screenshots at macOS sizes
  (1280x800 / 1440x900 / 2560x1600 / 2880x1800, all 16:10). iPad screenshots cannot fill it: they
  are 4:3, and Apple rejects them on dimensions. If your pipeline only produces an `.ipa` there is
  no build that can ever complete that version, and it sits in *Prepare for Submission* forever. It
  does not block your iOS submission — platforms submit independently — but delete the empty
  version rather than leaving it.

  The checkbox itself is a real decision, not a formality: leaving it on means Apple reviews your
  app **on macOS too**, where a VPN's packet-tunnel extension runs in a different environment
  (notably without the iOS memory cap). Test it on a Mac through TestFlight before you submit, or
  switch it off for the first release — you can enable it later without a new build, and a VPN that
  fails on macOS is a rejection rather than merely a bad review.

---

## 9. Test on TestFlight first

Every iOS build the pipeline produces goes to **TestFlight**, and that is also how a build reaches
App Store Connect at all — you pick it for your App Store version from the builds TestFlight
received. So test there before you submit.

**Fill in Test Information, or your app is invisible to testers.** TestFlight → *Test Information*
needs a **feedback email** and a **description**, set once per app. Until they exist, testers see
nothing — not an error, just an app that never appears in their TestFlight list — even though the
build finished processing and the tester is in the right group. Nothing anywhere tells you this is
the reason.

Two kinds of tester, and they behave very differently:

| | Internal | External |
| --- | --- | --- |
| Who | Your App Store Connect users | Anyone with an email or public link |
| Setup | Invite them under **Users and Access** first, then add them to the internal group — it does not work in the other order | Added straight to an external group |
| Beta App Review | Not required | **Required**, plus "What to Test" notes on the build |
| Availability | As soon as the build finishes processing | After beta review passes |

**An invited tester is not yet a tester.** Adding someone to a group only sends an invitation; their
state stays `INVITED` until they open the email and accept, and an app they have not accepted simply
never appears in their TestFlight — restarting the app or pulling to refresh does nothing, because
there is nothing there yet. Acceptance is **per app**, so someone already testing your other app
must accept again for this one. If the mail was lost, resend the invitation rather than re-adding
them to the group.

Two things worth knowing while testing:

- **Purchases run against the StoreKit sandbox.** TestFlight is therefore the right place to prove
  your subscription end-to-end — and the only place you can, before review. Set the sandbox account
  on the device first: **Settings → Developer → Sandbox Apple Account**, or the buy flow asks for a
  real payment method.
- **Testing on a Mac** needs the Apple Silicon Mac checkbox from step 8 enabled; TestFlight for Mac
  only offers apps that are Mac-available.

---

## 10. Submit

A submission is a **basket**, not a button. App Store Connect collects the things to be reviewed
together — your version, and anything else that needs review at the same time — and reviews them as
one unit. Build the basket, then send it.

1. **Pick the build.** On the version page, *Build → +*, and choose from what TestFlight received.
   Check the number rather than taking the newest: a build made before a fix you are relying on will
   ship that fix's absence to every user. Swapping the build later is free while the version is still
   editable, and impossible once it is in review.
2. **Add your subscriptions to the basket — from the products, not from the version.** This catches
   people because the submit page does not offer them. Open each subscription under *Subscriptions*
   and press **Add for Review** on the product itself; it joins the submission your version is
   already in. A first subscription is not reviewed on its own, so leaving it out ships an approved
   app with nothing to sell.

   **A new group brings itself, but not its products.** Editing group-level text (a display name,
   a translation) creates a pending *group version* that joins the submission on its own. The
   products do not follow. Submit like that and Apple refuses with *"New subscription groups must be
   submitted with an auto-renewable subscription from within that group"* — a new group with nothing
   buyable in it.

   Over the API, add the product's **version**, not the product. `reviewSubmissionItems` has no
   `subscription` relationship (`ENTITY_ERROR.RELATIONSHIP.UNKNOWN`, which reads like a permissions
   problem and is not); it has `subscriptionVersion`, pointing at an id from
   `/v1/subscriptions/<id>/versions`:

   ```jsonc
   POST /v1/reviewSubmissionItems
   { "data": { "type": "reviewSubmissionItems", "relationships": {
       "reviewSubmission":    { "data": { "type": "reviewSubmissions",    "id": "<submission id>" } },
       "subscriptionVersion": { "data": { "type": "subscriptionVersions", "id": "<version id>" } } } } }
   ```
3. **Write the review note.** A working test account and whatever a reviewer needs to reach every
   feature, including anything behind a purchase. Reviewers must be able to *use* the app, not just
   open it. The panel also wants your own contact details, so Apple can reach a human during review.
4. **Submit**, and wait.

If you are rejected, read the message literally: first rejections are usually about the paperwork
above, not about your app. Fixing metadata and resubmitting does not need a new build.
