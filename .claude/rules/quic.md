---
paths:
  - "**/*Quic*/**"
---

## QUIC

- Authenticated clients use QUIC as a stream transport with HTTP/1.1 admission and the existing tunnel
  framing. Anonymous peers see the HTTP/3 engine in `Net/VpnHood.Net.Http3`, which knows nothing of VpnHood;
  the server's proof filter, tunnel runner and budget plug into it, and only a valid protocol-15 proof admits a
  custom stream. Keep HTTP/3 parsing and authentication out of native transport adapters.
- Do not add ASP.NET Core or Kestrel as a production dependency (owner, 2026-10-30).
  Kestrel remains a reference in the differential tests only.
- Preserve Android/iOS providers and their memory limits. A server control-stream open must not block
  incoming requests when a client grants zero unidirectional stream credit. Release admission buffers
  after handoff; do not put HTTP/3 parsing or proof work on the tunnel's packet path.
- Migrating the actual tunnel to standard HTTP/3 through web frontends is separate future work.
  Keep admission encapsulated so that migration can replace it.
