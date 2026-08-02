# End-user legal documents

The documents in this folder are **binding, user-facing legal texts** — the ones users are shown,
not developer guidance (that lives in [../developer/](../developer/)). The website builds its
legal pages from these files: `www.vpnhood.com/<slug>` is rendered from `<slug>.md` here, fetched
from the `main` branch at site-build time (see `jekyll.yml` in the VpnHood.www repo). **Merging to
`main` is publication.**

Rules for editing:

- **PR only, never a direct push.** These files change what VpnHood promises its users; every
  change deserves a reviewer.
- **Bump the `Effective:` date** in the same PR as any substantive change. Stores and regulators
  care about *when* a policy changed; the git history is the audit trail, the Effective line is
  what users see.
- **Keep the text true to the code.** When a change to the apps alters what is collected or sent,
  update the affected policy in the *same PR* — that is why these files live in the code repo.
  The developer-facing analysis of what the Client actually collects is in
  [../developer/APP_STORE_PRIVACY.md](../developer/APP_STORE_PRIVACY.md); the two must never
  disagree.
- **File name = website slug.** Renaming a file breaks its public URL; don't.

**Forkers:** these are VpnHood's policies, describing VpnHood's servers and analytics under
VpnHood's name. Your fork must publish its own policy at its own URL — see
[../developer/APP_STORE_PRIVACY.md](../developer/APP_STORE_PRIVACY.md).

Currently here:

- [vpnhood-client-privacy-policy.md](vpnhood-client-privacy-policy.md) — VpnHood! CLIENT
- [vpnhood-connect-privacy-policy.md](vpnhood-connect-privacy-policy.md) — VpnHood! CONNECT

MANAGER's policy still lives in the GitHub wiki and migrates here later. The wiki copies of the
migrated policies are superseded by these files — update them to point here, don't edit them.
