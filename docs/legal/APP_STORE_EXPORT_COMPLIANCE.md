# App Store Export Compliance — publishing an iOS build of this repo

How to satisfy Apple's encryption/export-compliance requirements when you publish an iOS app built
from this repo (the vpnhood apps, or **your own fork/rebrand**). This is the operational companion to
[EXPORT_COMPLIANCE.md](EXPORT_COMPLIANCE.md) (the public cryptographic classification). It exists
because getting this wrong does not fail politely: the upload is rejected by altool with error
**90592** *"Invalid Export Compliance Code. The export compliance key value [] in the app's
Info.plist doesn't match the key value of the app's export compliance documentation"* — before the
build ever appears in App Store Connect, so there is nothing in the console to click.

> Not legal advice. It records what these regimes require and what this repo declares; a fork makes
> its own declarations under its own name.

## The two regimes (don't confuse them)

| | U.S. export law (BIS) | Apple App Store |
|---|---|---|
| What | Annual **self-classification report** under License Exception ENC | **Encryption declaration** on the app record in App Store Connect |
| Filed with | U.S. Bureau of Industry and Security + ENC Encryption Request Coordinator | Apple |
| Produces | Nothing you paste anywhere (the sent report IS the compliance) | A record on the app; **no code** on the non-France branch |
| Where checked | Nowhere in any console — keep the sent email | App Store Connect → your app → **App Information → App Encryption Documentation** |

A VPN built from this repo is **mass-market encryption software, ECCN 5D992.c** — no export license
is needed, but the two obligations above both apply. Filing with BIS does **not** update Apple, and
answering Apple's questionnaire does **not** file with BIS.

## A VPN is non-exempt — but the plists carry NO encryption keys (deliberate)

Two statements that sound contradictory and are both true:

1. **The app's encryption is non-exempt.** Apple's exemptions cover encryption limited to
   authentication/digital signatures, DRM, and a few niche categories. A VPN's core function is
   encrypting arbitrary user traffic — the textbook non-exempt case. "It only uses OS-provided
   crypto libraries" does **not** make an app exempt; it only makes the classification mass-market
   (5D992.c instead of a licensable ECCN). Every answer given to Apple must say non-exempt, and
   `ITSAppUsesNonExemptEncryption=false` in a plist would be a false statement — never do that.
2. **The plists must not carry `ITSAppUsesNonExemptEncryption` at all.** Declaring `true` in the
   binary obliges altool to find a matching Apple-issued `ITSEncryptionExportComplianceCode` next to
   it, and the non-France App Encryption Documentation flow (below) **never issues a code** — codes
   only exist on the France/uploaded-documentation branches. `true` without a code is therefore
   rejected with error 90592 on **every** upload, unconditionally (verified twice on 2026-07-30;
   the error fires identically whether or not the app record has documentation).

So the honest, working configuration is: **no `ITS*` keys in either plist** (host app + Network
Extension), and the non-exempt declaration made **on the App Store Connect app record** via the
one-time wizard below. Both plists carry a comment saying exactly this — read it before "fixing"
the absence of the key. Every build that has ever successfully uploaded (Client 826, Connect 830)
shipped without the key; the only uploads that ever failed were the ones that carried it.

## One-time setup in App Store Connect (per app)

1. **App Store Connect → Apps → *your app* → App Information → App Encryption Documentation →
   the `+` button.** This opens a 3-step wizard: functionality description → algorithm selection →
   French-store question. **No document is uploaded to Apple** — the declaration is created from
   the questionnaire answers alone (the BIS CSV below is a BIS-only artifact; keep it, but Apple
   never asks for it when France is answered No).
2. It first asks for **a short description of your app's functionality and purpose** — the field is
   limited to **300 characters**. Template (234 chars). GCM/CTR are named because they are the modes
   actually used for tunnel data; they are standard NIST modes of AES and do not change any
   questionnaire answer. The repository URL is not required:

   > Open-source VPN app. It encrypts user network traffic in a tunnel to a VPN server to protect
   > privacy. Encryption uses only standard algorithms (TLS, AES-GCM/CTR, RSA, SHA-2, HMAC) from
   > OS crypto libraries; no proprietary cryptography.

   The longer classification story (ECCN 5D992.c, License Exception ENC, the BIS annual report)
   does not fit in this field and does not need to — it lives in the BIS filing and in
   [EXPORT_COMPLIANCE.md](EXPORT_COMPLIANCE.md).

3. **Questionnaire answers** consistent with this codebase (see
   [EXPORT_COMPLIANCE.md](EXPORT_COMPLIANCE.md) for the algorithm inventory):
   - Uses encryption: **Yes**
   - Qualifies for an exemption: **No**
   - "Which encryption algorithms does your app implement":
     - *Encryption algorithms that are proprietary or not accepted as standard by international
       standard bodies (IEEE, IETF, ITU etc.)*: **do NOT select** — everything is standards-body
       cryptography.
     - *Standard encryption algorithms instead of, or in addition to, using or accessing the
       encryption within Apple's operating system*: **select** — the app ships the .NET crypto
       stack, composes AES-CTR at the application layer, and uses AES-GCM for tunnel data; that is
       encryption in addition to plain OS-API calls (even though the primitives route to
       CommonCrypto/CryptoKit on iOS).
4. **French store**: France regulates VPN/crypto apps separately (ANSSI declaration). If you have
   made no French filing, answer **No** for availability in France; add it later if you file.
   Answering No ends the wizard — there is no documentation-upload step.
5. That's it — **no code is issued on this branch**, and none is needed. The declaration lives on
   the app record; uploaded builds (whose plists carry no `ITS*` keys — see above) pick their
   compliance up from it. If TestFlight still shows **"Missing Compliance"** on a build, answer the
   prompt once with the same answers as the wizard (uses encryption: yes, non-exempt, standard
   algorithms) — never "doesn't use encryption", which is false for a VPN.

## The annual BIS self-classification report

Must be **received by February 1** of the year after the calendar year of export (15 CFR
§740.17(e)(3); report format per Supplement No. 8 to Part 742 of the EAR — **CSV is the only
accepted format**). Email the completed CSV to **<crypt-supp8@bis.doc.gov>** and **<enc@nsa.gov>**.
BIS publishes the sample and rules at
<https://www.bis.gov/learn-support/encryption-controls/annual-self-classification>. Cover-email
template (this is what the vpnhood apps file):

> To Whom It May Concern,
>
> Please find attached our annual self-classification report submitted under License Exception ENC,
> 15 CFR §740.17(b)(1), in accordance with Supplement No. 8 to Part 742 of the EAR.
>
> Product: `<AppName>`
> ECCN: 5D992.c
> The product uses only standard published encryption algorithms (AES, RSA, TLS, SHA-256,
> HMAC-SHA256) implemented via standard OS/platform cryptographic libraries. Source code is publicly
> available.
>
> Contact: `<name, compliance email, phone>`

BIS does not send a confirmation for these reports; your sent email is the record. There is no
console anywhere to "check" it — Apple never sees this filing and never reflects it.

## Verifying what Apple has on record

The App Store Connect **API** answers some of it, with the same API key CI uses for uploads:

```text
GET /v1/apps?filter[bundleId]=<bundle id>              -> app id
GET /v1/builds?filter[app]=<id>&sort=-uploadedDate     -> per-build usesNonExemptEncryption
GET /v1/appEncryptionDeclarations?filter[app]=<id>     -> documentation-based declarations only
```

Caveat learned the hard way (2026-07-30): entries created by the console's App Encryption
Documentation **wizard do not appear** in `appEncryptionDeclarations` (it returned zero team-wide
while altool could clearly see the record) — the console UI is the only view of wizard entries. The
per-build `usesNonExemptEncryption` on `builds` is reliable and shows what each shipped build
actually declared.

## Status of the vpnhood apps (maintainers)

- **Client** (`com.vpnhood.client.ios`): the `true` key added 2026-07-06 (`bda96bae5`) blocked
  every upload after build 826 with error 90592; **removed 2026-07-31** after the wizard was
  completed on the app record (France: No → no code). Plists now carry no `ITS*` keys, by design.
  Note: build 826's per-build answer was recorded as `usesNonExemptEncryption=False` (a quick UI
  click) — wrong for a VPN; answer non-exempt from now on.
- **Connect** (`com.vpnhood.connect.ios`): plists carry no encryption keys (same as Client now).
  Its app record has **not** had the wizard completed — run the one-time setup above for Connect
  too, so its builds stop needing the per-build "Missing Compliance" click (its build 830 was also
  recorded as `False`).
- BIS annual self-classification: filed (2026) — see contact in
  [EXPORT_COMPLIANCE.md](EXPORT_COMPLIANCE.md).
- The **private** filing kit (the CSV with entity/contact details, exact send steps, and the
  filing checklist) is deliberately outside this repo: `.user/legals/FILING-INSTRUCTIONS.md`
  (sibling of the repo root, never published). This public doc must stay PII-free.
