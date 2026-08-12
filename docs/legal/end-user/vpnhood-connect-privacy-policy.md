# VpnHood! CONNECT - Privacy Policy

*Effective: 2026-08-12*

**PLEASE NOTE:** This privacy policy applies to the official **VpnHood! CONNECT** app and to the VPN servers **we operate**. Unlike VpnHood! CLIENT, CONNECT comes with its own built-in access and does not let you add another provider's server, so the servers you use are ours and this policy covers them.

This policy describes what the app collects, what it never collects, and what our servers record. By using the app, you agree to the practices described here. We do not use or share your information except as described in this policy.

## What VpnHood! CONNECT Collects

The app collects **anonymous usage and diagnostic data** and sends it to Google Analytics (through Google's Firebase service). This data does not identify you; examples are how often the app is launched, which screens are used, the operating system version, connection results, and the technical text of error messages. Our open-source code shows what is sent and when.

Some data depends on **where you installed the app from**, because those builds contain different components:

| | App&nbsp;Store (iOS) | Windows, Linux | Google&nbsp;Play (Android) | Our&nbsp;website (Android) |
| --- | --- | --- | --- | --- |
| Anonymous analytics | yes | yes | yes | yes |
| Crash reports | no | no | yes | no |
| Advertisements | no | no | yes | no |
| Optional sign-in and purchases | yes | no | yes | no |
| Install attribution | no | no | no | in China only |

### Your Client ID

The app identifies itself with a **Client ID**. It is never your device's serial number, phone number, or advertising ID, and it is never sent in its raw form — what leaves your device is a one-way hash that also mixes in the app's identity, so two VpnHood apps on the same device have unrelated Client IDs and neither can be traced back to the original value.

On most builds the underlying value is a random one created inside the app on first launch, so deleting and reinstalling the app produces a brand-new Client ID. On the Android build downloaded from our website, it is derived from the Android system identifier instead, which means it stays the same if you reinstall.

The Client ID labels the anonymous analytics below and is sent to our VPN servers for session management, quotas, and abuse prevention.

### You can turn analytics off

Analytics is controlled by **Settings → Privacy → "Share anonymous usage data"** in the app. It is on by default; turning it off stops analytics events **and crash reports** from being sent, and turning it off while the app is running takes effect immediately and is remembered for later launches. Turning it off also disables in-app bug-report and feedback sending, since those use the same channel.

### Technical information

When analytics is on, the following is collected:

- Client ID (the identifier described above)
- VpnHood version
- Country (derived by Google from the connection, not reported by the app)
- Language
- OS name and version
- Device model (if applicable)
- Device architecture and browser engine
- Session start time and duration, and the app screens you visit
- Connection results — the server location you chose, whether the connection succeeded, and the server address used
- The amount of traffic (bytes sent and received) and the number of connections, reported periodically while connected
- Error messages shown by the app (their English technical text)

### Crash reports (Google Play build)

The Google Play build sends automatic crash reports through Google Firebase Crashlytics. A crash report contains the technical details of the failure and device information; it never includes your browsing activity or the content of your traffic.

Crash reports follow the same switch as analytics, and your choice is remembered from one launch to the next. The single exception is the very first start of a fresh install: the crash handler has to be in place before your settings can be read, so a crash during that first startup — exactly the kind we most need to fix — is still reported.

### Advertisements (Google Play build)

The Google Play build shows advertisements, including rewarded ads you may choose to watch to extend a session. Ads are delivered by **Google AdMob**, which collects its own data under Google's policies to select and measure ads. Advertising is not part of the anonymous analytics above and is not controlled by the analytics switch. Ads are not shown in every country.

### Optional sign-in and purchases (App Store and Google Play builds)

You can use the app without an account. Signing in is only ever needed to buy or restore a subscription, and each build offers the one sign-in its platform provides:

- **App Store build (iOS)** — **Sign in with Apple**. We receive your **email address**, which may be a private relay address that Apple generates for you (`…@privaterelay.appleid.com`) if you choose to hide your real one; a relay address works exactly as well for us. Payments are processed by **Apple**; we never see your card details.
- **Google Play build (Android)** — **Sign in with Google**. We receive your **email address and basic public profile information** from Google. Payments are processed by **Google Play**; we never see your card details.

Either way we store that email address with your account so your subscription follows you across your devices, and you can delete it at any time — see [Delete Your Account](#delete-your-account-forget-me). The Windows and Linux builds have no sign-in and no in-app purchases.

### Install attribution (website build for Android, China only)

The Android build downloaded from our website contains **AppsFlyer**, which tells us which campaign or link an install came from — necessary where Google Play is unavailable. It starts **only if your device region is China**; everywhere else it is skipped and sends nothing. Advertising identifiers are explicitly disabled for it. The App Store, Google Play, Windows, and Linux builds do not contain it.

## Log Data (our VPN servers)

When you connect, our servers record what any VPN server must see to run the service:

- Your Client ID and the access your app uses to connect
- The technical information listed above
- The amount of traffic (bytes sent and received), used for accounting and quotas
- Your email address, if you have signed in
- Your IP address and connection activity — the time and your client endpoint (IP address & port) — kept in server log files for **30 days**, without backups. If our hosting provider forwards a "Notice of Claimed Infringement," we use these log files to identify, notify, or suspend the offending account.

**Important!** We do not record your browsing. Our servers never extract the destinations you visit — domains, URLs, or IP addresses — from your traffic, so there is nothing about them to log, store, or hand over.

When you use the **Split Domain** feature, the app reads domain names on your device to decide which traffic to send through the VPN. That happens inside the app, on your device, and is never sent to us.

Connection activity stays only in server log files; it never enters our database. Our database stores the technical information associated with your Client ID, and your email address if you signed in.

## Delete Your Account (Forget Me)

If you have signed in, you can permanently delete your account at any time:

- **In the app**: Account page → **Delete my account**.
- **Without the app**: follow the steps on our [account deletion page](https://www.vpnhood.com/user-account-deletion-request).

Deletion applies everywhere at once: you are signed out on all devices, your sign-in identity and
email address are erased, and there is no way to restore the account — signing in again later
creates a new, empty one.

What deletion does **not** do:

- It does not cancel a subscription. Subscriptions are billed by the store where you purchased
  them, and only you can cancel there — before or after deleting. Access you have already paid for
  keeps working until the current period ends.
- It does not erase invoices and payment records. We are legally required to keep them for
  accounting and tax purposes, but they are anonymized: your name and email address are replaced
  with placeholders.
- Residual copies of your data may remain in our server logs and backups for up to **30 days**
  after deletion, after which they expire.

If you also have services purchased on our website, those form a separate billing relationship:
deletion is refused until you cancel them in the web client area, so you are never left with a
running payment you cannot manage. Companies that processed your data in their own right — your
sign-in provider, the app store that billed you, and payment processors — retain their own records
under their own published policies.

Separately from deletion: if we process a refund for a purchase made **on our website**, we keep an
anonymous one-way hash of the refunded account's email address for up to **24 months**, used only to
evaluate future refund requests (fraud prevention). It cannot be turned back into your address and
survives account deletion. Refunds of app-store purchases are decided by the store and leave no such
record with us.

## Service Providers

These companies process data on our behalf or in their own right, and only for the purposes described above:

- **Google LLC** — Google Analytics / Firebase (anonymous analytics, all builds), Firebase Crashlytics (crash reports, Google Play build), Firebase storage (reports and ratings you send), AdMob (advertising, Google Play build), Google Sign-In and Google Play billing (optional accounts and purchases, Google Play build)
- **Apple Inc.** — Sign in with Apple and App Store billing (optional accounts and purchases, App Store build)
- **AppsFlyer** — install attribution, website build for Android only

They are obliged not to use the data for any purpose other than the one we assign them, except where they act as independent controllers under their own published policies (advertising and payments).

## Android Permissions

The `QUERY_ALL_PACKAGES` permission is used to allow the user to select which apps are allowed/disallowed to use the VPN.

## Children's Privacy

Our services are not directed to anyone under the age of 18. We do not knowingly collect personal information from anyone under 18. If we discover that a minor has provided us with personal information, we immediately delete it from our servers. If you are a parent or guardian and you are aware that your child has provided us with personal information, please contact us so that we can take the necessary actions.

## Client Feedback & Bug Report

The app lets you send us feedback, a rating, or a diagnostic log file to help solve technical issues. **Nothing is ever sent automatically** — a report leaves your device only when you press the send button. An email field is optionally available if you would like a response from us. The log file contains basic technical information, and sensitive details that are not required for debugging, such as your IP address, are automatically removed before sending.

## Changes to This Privacy Policy

We may update this policy from time to time; the current version is always available on this page, and changes take effect when posted here. Every revision is recorded in our open-source repository — see the [change history of this policy](https://github.com/vpnhood/VpnHood/commits/develop/docs/legal/end-user/vpnhood-connect-privacy-policy.md).

## Contact Us

Questions about this policy: [support@vpnhood.com](mailto:support@vpnhood.com)
