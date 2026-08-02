# App Store Export Compliance — iOS builds

What this repo's iOS apps (and any fork) must do about Apple's encryption/export-compliance
questions. Companion to [EXPORT_COMPLIANCE.md](EXPORT_COMPLIANCE.md) (the public cryptographic
classification), [APP_STORE_PRIVACY.md](APP_STORE_PRIVACY.md) (the App Privacy questionnaire and
other App Store legal checkpoints), and [APP_STORE_TERRITORIES.md](APP_STORE_TERRITORIES.md)
(which storefronts to deselect). Not legal advice; a fork makes its own declarations under its
own name.

## TL;DR — it is already handled

All four Info.plists (Client + Connect, host app + Network Extension) declare:

```xml
<key>ITSAppUsesNonExemptEncryption</key>
<false/>
```

With that key in place there is **nothing to do in App Store Connect**: no wizard, no documents to
upload, no compliance code, no per-build "Missing Compliance" questions. Uploads just pass. The one
recurring obligation is the **annual BIS report** (below) — a U.S.-law duty that Apple neither asks
about nor waives.

`false` does **not** claim the app has no cryptography. It is Apple's machine-readable form of
*"this app needs no Apple export-compliance documentation"* — which Apple's own questionnaire
concludes for this app: it uses only **standards-body algorithms** (AES-GCM/CTR — NIST SP
800-38D/38A, RSA — RFC 8017, SHA-2 — FIPS 180-4, HMAC — RFC 2104, TLS — IETF; nothing
proprietary) and is **not distributed on the French store**. Apple's exact conclusion for those
answers: *"Based on your answers, you don't need to upload any documents. You can specify that you
don't use encryption in the Info.plist to avoid answering encryption questions with each app
submission."*

## ⚠️ The trap: never set this key to `true`

`true` looks like the honest value for a VPN — it is not, and it is a costly mistake:

- `true` obliges Apple's uploader to find a matching **`ITSEncryptionExportComplianceCode`** in the
  binary. Such codes are only ever issued on the proprietary-algorithms / France branches — the
  branch this app is on (**standard algorithms, no France**) **never issues one**.
- Every upload is then rejected with error **90592** ("Invalid Export Compliance Code … key value
  [] doesn't match"), *before* the build reaches App Store Connect — so there is nothing in the
  console to click, and the error sends you hunting for a code that does not exist.
- This exact misconfiguration silently blocked every Client iOS upload from 2026-07-06 to
  2026-07-31 and took many hours to diagnose. The plists carry comments so nobody "fixes" the
  `false` back to `true`; this section is the long form of those comments.

Deleting the key entirely also works for uploading, but then **every build** asks the compliance
question in TestFlight — `false` answers it once and forever.

## When `false` stops being valid

Re-open this topic only if one of these changes:

1. **Proprietary/non-standard cryptography is added** (a self-designed or unpublished cipher —
   composing standard NIST modes like CTR in app code does *not* count). Apple then requires real
   documentation and issues a code.
2. **Distribution in France** is enabled. France requires an ANSSI import declaration in addition
   to everything here; Apple's questionnaire branches to a documentation upload for it.

In both cases: App Store Connect → the app → **App Information → App Encryption Documentation →
`+`** opens the questionnaire (short app description ≤300 characters, algorithm categories, France
question). For the current codebase the honest answers are: uses encryption **yes**; proprietary
algorithms **no**; *"standard encryption algorithms instead of, or in addition to, using or
accessing the encryption within Apple's operating system"* **yes**; France **no** — which ends with
the "no documents needed" conclusion quoted above. Description text that fits the field (234
chars):

> Open-source VPN app. It encrypts user network traffic in a tunnel to a VPN server to protect
> privacy. Encryption uses only standard algorithms (TLS, AES-GCM/CTR, RSA, SHA-2, HMAC) from
> OS crypto libraries; no proprietary cryptography.

## The annual BIS self-classification report (recurring — every year)

Apple's "no documents needed" covers **Apple only**. Apps on the App Store are exported from
Apple's U.S. servers, so U.S. export law applies regardless: the app is mass-market encryption
software, **ECCN 5D992.c**, authorized under License Exception ENC (15 CFR §740.17(b)(1)) — no
license, but an **annual self-classification report** is required (15 CFR §740.17(e)(3),
Supplement No. 8 to Part 742):

- **Format:** CSV (the only accepted format); sample and rules at
  <https://www.bis.gov/learn-support/encryption-controls/annual-self-classification>.
- **Send to:** <crypt-supp8@bis.doc.gov> and <enc@nsa.gov>.
- **Deadline:** received by **February 1** of the year after the calendar year of export.
- BIS sends no confirmation — the sent email + CSV are the record. No console anywhere reflects it.
- A fork files **its own** report under its own entity name.

Cover-email template:

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

## Verifying what Apple has on record

With the App Store Connect API (same key CI uses): per-build state is on `/v1/builds`
(`usesNonExemptEncryption` — with the plist key set it reads `False` automatically). Note that
`/v1/appEncryptionDeclarations` does **not** show questionnaire entries created in the console UI
(verified 2026-07-30: it returned zero while the record clearly existed) — don't let an empty
response mislead you.

## Status (maintainers)

- Plists set to `false` on 2026-07-31 (Client build 836 was the first prompt-free upload). The
  `true` misconfiguration existed 2026-07-06 → 2026-07-31 and blocked all Client uploads.
- BIS annual self-classification: filed 2026 — contact in
  [EXPORT_COMPLIANCE.md](EXPORT_COMPLIANCE.md). Next one due by 2027-02-01.
- The **private** filing kit (CSV with entity/contact details, send steps, checklist) is
  deliberately outside this repo: `.user/legals/FILING-INSTRUCTIONS.md` — never published; this
  public doc stays PII-free.
