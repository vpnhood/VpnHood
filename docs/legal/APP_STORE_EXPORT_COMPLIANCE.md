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

## The plists declare `ITSAppUsesNonExemptEncryption = false` — what that means

`false` does **not** claim "this app has no cryptography". It is Apple's machine-readable form of
"this app uses no encryption **that requires Apple export-compliance documentation**". Apple's own
wizard (below), given the honest answers — uses encryption: yes; **standard** algorithms
(AES-GCM/CTR, RSA, SHA-2, HMAC, TLS — all NIST/IETF/ISO); nothing proprietary; not on the French
store — concludes: *"Based on your answers, you don't need to upload any documents. You can specify
that you don't use encryption in the Info.plist to avoid answering encryption questions with each
app submission."* That plist value is `false`. So `false` is the Apple-prescribed encoding of
exactly those answers, and it makes every upload pass with **no per-build compliance questions**.

Do not confuse this with the EAR meaning of "exempt": under U.S. export law the app is mass-market
**5D992.c** — authorized without a license, but still owing the annual BIS self-classification
report (below). The plist key is an Apple-documentation question; the BIS report is the legal duty.
The two are independent, and `false` changes nothing about the BIS obligation.

**Never set the key to `true`.** `true` obliges altool to find a matching Apple-issued
`ITSEncryptionExportComplianceCode` in the binary, and the non-France flow **never issues a code**
(codes exist only on the proprietary-algorithms / France branches). `true` without a code is
rejected with error 90592 on every upload, unconditionally — verified twice on 2026-07-30, with and
without documentation on the app record. That misconfiguration silently blocked all Client iOS
uploads between 2026-07-06 and 2026-07-31.

The key + explanatory comment lives in **all four** plists (Client + Connect, host app + Network
Extension). "Proprietary or not accepted as standard" means self-designed/unpublished ciphers; this
codebase implements none — AES is FIPS 197/ISO 18033-3, GCM is SP 800-38D/RFC 5288 (the mandatory
TLS 1.3 cipher), CTR is SP 800-38A, RSA is RFC 8017, SHA-2 is FIPS 180-4, HMAC is RFC 2104.

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
5. That's it — the wizard ends with *"you don't need to upload any documents"* and points at the
   plist: with `ITSAppUsesNonExemptEncryption=false` in every plist (see above), builds skip the
   compliance question entirely and **no code is ever issued or needed**. If an older build (built
   before the plist carried `false`) shows **"Missing Compliance"** in TestFlight, answer its
   prompt with the same wizard answers — standard algorithms, nothing proprietary — which records
   as `usesNonExemptEncryption=False`, consistent with the plist.

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
  every upload after build 826 with error 90592. 2026-07-31: wizard completed on the app record,
  key set to **`false`** in both plists per the wizard's own conclusion. Builds 826 and 835 were
  answered/recorded as `usesNonExemptEncryption=False` — consistent with this framework.
- **Connect** (`com.vpnhood.connect.ios`): both plists set to `false` on 2026-07-31 (same commit),
  so its next upload skips the per-build "Missing Compliance" click that build 830 needed.
  Optionally run the one-time wizard on its app record too, so the description/answers are on file
  there as well.
- BIS annual self-classification: filed (2026) — see contact in
  [EXPORT_COMPLIANCE.md](EXPORT_COMPLIANCE.md).
- The **private** filing kit (the CSV with entity/contact details, exact send steps, and the
  filing checklist) is deliberately outside this repo: `.user/legals/FILING-INSTRUCTIONS.md`
  (sibling of the repo root, never published). This public doc must stay PII-free.
