# Google Play — the steps only you can do

Everything on this page happens in a browser, by hand, in **your** Google accounts. The pipeline can
upload builds and update the listing text and screenshots, but only after the app exists and the
paperwork below is done.

Work through it in order. Come back to [start here](README.md) when you are done.

---

## 1. Open the developer account

Register at the **Google Play Console** (one-off fee, currently about 25 USD). Google then verifies
your identity: your legal name or company details, address and phone number. For a company account
this usually includes a **D‑U‑N‑S number**, the same one Apple asks for.

Register as an **organization**, not an individual. You need an organization for Apple anyway, and
Google applies extra hurdles to new personal accounts — including a period of closed testing with a
minimum number of real testers before production access is granted. Check the current rule in the
console; it has changed several times.

If you plan to sell subscriptions, also set up a **payments profile** with your bank and tax
details. Until that exists, you cannot create products.

---

## 2. Create the app

In Play Console → **All apps → Create app**. You choose:

- the app **name** shown on the store,
- default language, app-or-game, and free-or-paid — a **free app can never be switched to paid
  later**, so if in doubt choose free (an app with in-app subscriptions is still "free"),
- the declarations Google asks for up front.

Then decide the **package name** (for example `com.yourcompany.yourapp`). Like Apple's identifier,
it is permanent and must match what the pipeline builds. If you also want a version distributed
outside Play as a downloadable file, that build uses a **different** package name so that both can
be installed side by side.

---

## 3. Let Google hold your signing key

Turn on **Play App Signing** (the default for new apps). Google keeps the key that ultimately signs
what users install; the key our pipeline uses becomes your **upload key**.

This matters more than it sounds:

- If the upload key is ever lost or stolen, Google can issue you a new one — recoverable.
- Without Play App Signing, losing your key means you can **never update your app again**. Users
  would have to uninstall and reinstall a differently-signed app.

The pipeline generates and uses that upload key from what you paste into your project's settings —
see [deployment](../cicd/deployment.md). Keep a backup of it somewhere safe and private.

---

## 4. Create the robot account the pipeline uses

Google does not accept your password for automation. Instead you create a **service account** — a
robot with its own credentials — and give it permission to publish your app:

1. In Play Console → **Setup → API access**, create (or link) a Google Cloud project.
2. Create a **service account** and download its **JSON key file**.
3. Back in Play Console, **grant that service account access** to your app, with permission to
   release to the tracks you use and to edit the store listing.

That JSON file is what you paste into your project's settings. Nothing else about Google Cloud
matters for this.

---

## 5. Fill in the store paperwork

Play blocks release until every one of these is complete. All are in the console, under **Policy**
and **Grow → Store presence**:

1. **Store listing** — name, short and full description, graphics. Text and screenshots are
   generated and uploaded for you, so you can leave these to the pipeline; the **feature graphic**
   and **app icon** are yours to supply.
2. **Privacy policy URL** — a working page on your own domain.
3. **App content declarations**, each a short questionnaire:
   - **Data safety** — what you collect and share. Answer from your own app's behaviour, not from
     someone else's form.
   - **Content rating** — honest answers for a VPN put it in a mature category.
   - **Target audience** — a VPN is not for children; saying otherwise triggers extra rules.
   - **Ads** — declare whether your app shows ads.
   - **Permissions** — a VPN app uses Android's VPN service, and Google asks you to justify it.
     Answer plainly: the app is a VPN and routes the user's traffic on request.
4. **Countries and regions** — choose where the app is available. Several countries restrict or ban
   VPN apps; the reasoning we use for Apple applies equally here:
   [territories](../legal/developer/APP_STORE_TERRITORIES.md).

---

## 6. Subscriptions

Skip if your first release is free.

In Play Console → **Monetize → Products → Subscriptions**, create one product per plan. Each needs a
**product id that exactly matches** what your billing portal expects — the app carries no fallback
list, so a mismatch means the plan simply cannot be sold. Give each a base plan, a price, and a
description.

To receive renewals, cancellations and refunds, set up **real-time developer notifications** so
Google can call your billing portal. Add **license testers** (Play Console → Setup → License
testing) so you can buy your own subscriptions without being charged.

---

## 7. The first release

1. **Make your first release a pre-release.** The pipeline chooses the track for you from the kind
   of release you ask for: a pre-release goes to a **testing** track, a normal release goes straight
   to **production**. Publish the test build, install it on a real phone, and only then do a normal
   release — this is your last chance to catch a broken build cheaply.
2. Google reviews it. First reviews of a VPN app take longer than average and can come back with
   policy questions rather than technical ones.
3. **Roll out gradually.** The publish step accepts a percentage; start small. A bad release caught
   at 5% is a much smaller problem than one at 100%.
4. Note that the store *page* is a separate publish, and Google will not accept listing updates
   until something has actually been released on the production track.

---

## 8. Afterwards

Later releases are automatic: the pipeline builds, uploads and updates the listing. You return to
the console only when Google asks you to re-confirm a declaration, or when you change something
that needs review.

One recurring chore: Google raises the minimum Android version it accepts every year. When that
happens the pipeline needs a settings change — not something you can fix in the console.
