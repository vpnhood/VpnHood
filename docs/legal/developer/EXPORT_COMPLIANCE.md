# Export Compliance — Encryption

> Public statement of the cryptographic classification of VpnHood for U.S. export
> control and Apple App Store purposes. Contains no personal information.

## Summary

VpnHood is an open-source VPN. It uses **only standard, published cryptographic
algorithms** provided through operating-system and platform cryptographic
libraries. It contains **no proprietary or non-standard cryptography**.

## Classification

| Item | Value |
|------|-------|
| Product | VpnHood (clients, server, and libraries) |
| ECCN | **5D992.c** |
| Export authorization | License Exception ENC — 15 CFR §740.17(b)(1) |
| U.S. export status | Mass-market encryption software; standard algorithms only |

## Cryptography used

| Algorithm | Use | Source |
|-----------|-----|--------|
| AES (128 / 256-bit) — GCM, CBC | Tunnel data, token/config encryption | Platform crypto library |
| RSA (2048-bit) | X.509 certificates / key transport | Platform crypto library |
| TLS 1.2 / 1.3 | Transport security (TCP and QUIC channels) | OS / platform TLS |
| SHA-256 | Hashing, handshakes, integrity | Platform crypto library |
| HMAC-SHA256 | Message authentication | Platform crypto library |
| SHA-1 | **Non-security uses only:** the WebSocket handshake accept-key mandated by RFC 6455, and deriving a unique Windows network-adapter name. Not used for authentication, signatures or confidentiality | Platform crypto library |

All algorithms are accepted standards published by international standards bodies
(IETF, IEEE, ITU). They are implemented via standard operating-system and .NET
cryptographic libraries (`System.Security.Cryptography`, `SslStream`, TLS). There
are **no custom cipher primitives**.

One further AES use is not data encryption and so is not listed above: a single AES block
operation computes the QUIC **header-protection** mask exactly as RFC 9001 §5.4.3 prescribes, so
the app can read the SNI of a QUIC packet for domain filtering. It protects no user data and
carries no key of ours.

## Publicly available source code

VpnHood's complete source code — including all cryptographic implementations — is
publicly available at: **https://github.com/vpnhood/VpnHood**

## Filings

An annual self-classification report for these items is submitted to the U.S.
Bureau of Industry and Security (BIS) and the ENC Encryption Request Coordinator
in accordance with 15 CFR §740.17(e) and Supplement No. 8 to Part 742 of the EAR.

## Contact

Export-compliance enquiries: **compliance@omegahood.com**

---

_Last updated: 2026-08-29 · This document is informational and does not constitute
legal advice._

> **Single source.** This table is the authoritative algorithm inventory. The App Store
> questionnaire guide ([APP_STORE_EXPORT_COMPLIANCE.md](APP_STORE_EXPORT_COMPLIANCE.md)) links here
> rather than restating it — the two lists had drifted apart once, which is exactly the kind of
> discrepancy between two public statements this document exists to avoid. When the cryptography
> changes, change it **here**.
