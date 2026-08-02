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
| AES (128 / 256-bit) — GCM, CBC, CTR | Tunnel data, token/config encryption | Platform crypto library |
| RSA (2048-bit) | X.509 certificates / key transport | Platform crypto library |
| TLS 1.2 / 1.3 | Transport security (TCP and QUIC channels) | OS / platform TLS |
| SHA-1, SHA-256 | Hashing, handshakes | Platform crypto library |
| HMAC-SHA256 | Message authentication | Platform crypto library |

All algorithms are accepted standards published by international standards bodies
(IETF, IEEE, ITU). They are implemented via standard operating-system and .NET
cryptographic libraries (`System.Security.Cryptography`, `SslStream`, TLS). There
are **no custom cipher primitives**.

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

_Last updated: 2026-07-06 · This document is informational and does not constitute
legal advice._
