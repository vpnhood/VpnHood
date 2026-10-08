# End-user legal documents

The privacy policies and terms of use VpnHood's own apps show their users: **binding, user-facing
legal texts**, not developer guidance (that lives in [../developer/](../developer/)). They describe
VpnHood's apps, servers and analytics, under VpnHood's name.

**Forkers:** these are not yours to ship. Your app needs its own privacy policy and terms of use, on
your own website, describing what your build sends and to whom; the app links to them from its
settings (`Links.PrivacyPolicy` and `Links.TermsOfUse` in its appsettings).
[../developer/APP_STORE_PRIVACY.md](../developer/APP_STORE_PRIVACY.md) walks through the store
questionnaires.

Whoever keeps a policy, VpnHood or a fork, keeps two places true together, the website and the app:

- **The policy follows the code.** When a change alters what the app collects or sends, the policy
  changes with it, and so does its `Effective:` date: stores and regulators care about *when* a
  policy changed. [../developer/APP_STORE_PRIVACY.md](../developer/APP_STORE_PRIVACY.md) analyses
  what the app actually collects; the two must never disagree.
- **The app's own words follow the policy.** The app describes its data in its own words too, and a
  fork shows VpnHood's until it replaces them: the Privacy page's notice and the settings'
  descriptions (`ANONYMOUS_TRACKER_NOTICE`, `ALLOW_ANONYMOUS_TRACKER_DESC`, `PRIVACY_DESC`), the
  account-deletion words (`DELETE_MY_ACCOUNT_DESC`, `CONFIRM_DELETE_ACCOUNT_DESC`, the
  `DELETE_ACCOUNT_*` notes) and the first-run summary the app names by `PrivacyConsentAssetName`
  (`privacy-consent-client.md`, `privacy-consent-connect.md`). A zip of the fork's own ahead of the
  UI's (`AppOptions.UiZipAssets`) replaces them, file by file. A summary may leave things out; it
  never says otherwise.

Currently here:

- [vpnhood-client-privacy-policy.md](vpnhood-client-privacy-policy.md) — VpnHood! CLIENT (the app only; CLIENT is bring-your-own-key, so server behaviour is out of its scope)
- [vpnhood-client-terms-of-use.md](vpnhood-client-terms-of-use.md) — VpnHood! CLIENT licence and acceptable-use terms
- [vpnhood-connect-privacy-policy.md](vpnhood-connect-privacy-policy.md) — VpnHood! CONNECT (app **and** our servers, since CONNECT only uses ours)
- [vpnhood-connect-terms-of-use.md](vpnhood-connect-terms-of-use.md) — VpnHood! CONNECT licence, service, billing, and acceptable-use terms
- [vpnhood-manager-privacy-policy.md](vpnhood-manager-privacy-policy.md) — VpnHood! MANAGER
- [vpnhood-manager-terms-of-use.md](vpnhood-manager-terms-of-use.md) — VpnHood! MANAGER terms of use
