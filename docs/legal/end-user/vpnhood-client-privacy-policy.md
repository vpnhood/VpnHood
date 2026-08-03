# VpnHood! CLIENT - Privacy Policy

*Effective: 2026-08-02*

**PLEASE NOTE:** This privacy policy applies to the official **VpnHood! CLIENT** app. VpnHood! CLIENT works with an access key you obtain yourself; your VPN traffic is handled by the server that key belongs to, under its operator's privacy policy, which is beyond our control.

This policy describes what the VpnHood! CLIENT app collects and what it never collects. By using the app, you agree to the practices described here. We do not use or share your information except as described in this policy.

## What VpnHood! CLIENT Collects

The app collects **anonymous usage and diagnostic data** and sends it to Google Analytics (through the Firebase SDK). This data does not identify you or your device; examples are how often the app is launched, which screens are used, the operating system version, and the technical text of error messages. The app contains **no crash-reporting SDK, no advertising SDK, and no user accounts** — there is no login, no name, no email, and no payment information inside the app. Our open-source code shows that we don't send any user-identifiable information anywhere.

### Your Client ID

The app identifies itself with a **Client ID**. It is never your device's serial number, phone number, or advertising ID, and it is never sent in its raw form — what leaves your device is a one-way hash that also mixes in the app's identity, so two VpnHood apps on the same device have unrelated Client IDs and neither can be traced back to the original value.

On mobile and Linux the underlying value is a random one created inside the app on first launch, so deleting and reinstalling the app produces a brand-new Client ID. On Windows it is derived from your Windows user account instead, which means it stays the same if you reinstall.

The Client ID is used in two places: it labels the anonymous analytics described above, and it is sent to the VPN server you connect to for session management and abuse prevention.

### You can turn analytics off

Analytics is controlled by **Settings → Privacy → "Share anonymous usage data"** in the app. It is on by default; when you turn it off, the analytics component is not merely muted — it is never loaded at all, so nothing is sent to Google Analytics. If you turn it off while the app is running, collection stops immediately. Turning it off also disables in-app bug-report and feedback sending, since those use the same channel.

### Technical information

When analytics is on, the following is collected:

- Client ID (the random identifier described above)
- VpnHood version
- Country (derived by Google from the connection, not reported by the app)
- Language
- OS name and version
- Device model (if applicable)
- Browser engine
- Session start time and duration, and the app screens you visit
- Error messages shown by the app (their English technical text)

The app does **not** send your traffic volume to analytics; bytes are counted by the VPN server you connect to (see "VPN Servers").

*Note:* If the server you connect to provides its own analytics ID, the app also reports a single anonymous connection event to that server operator's Google Analytics. The same switch above controls this.

## VPN Servers

VpnHood! CLIENT ships with no server and no access key — you choose the server by the access key you add. Whoever operates that server — you, a third party, or us — necessarily sees what any VPN server sees: your IP address, connection times, and traffic totals. Your VPN traffic is governed by that operator's privacy policy, not this document.

If you connect to a server we operate, we keep connection logs (time, IP address and port, Client ID) for 30 days without backups, only to answer abuse complaints.

**Your destinations are never recorded.** VpnHood servers do not extract the domains, URLs, or IP addresses you visit from your traffic, so there is nothing about them to log, store, or hand over. When you use the **Split Domain** feature, the app reads domain names on your device to decide which traffic to send through the VPN — that happens inside the app, on your device, and is never sent to us.

## Service Providers

The only third party that processes app data on our behalf is **Google LLC**: Google Analytics/Firebase for the anonymous analytics described above, and Google Firebase storage for the reports and ratings you choose to send. Google processes this data to provide these services to us and is not permitted to use it for other purposes.

## Android Permissions

The `QUERY_ALL_PACKAGES` permission is used to allow the user to select which apps are allowed/disallowed to use the VPN.

## Children's Privacy

Our services are not directed to anyone under the age of 18. We do not knowingly collect personal information from anyone under 18. If we discover that a minor has provided us with personal information, we immediately delete it from our servers. If you are a parent or guardian and you are aware that your child has provided us with personal information, please contact us so that we can take the necessary actions.

## Client Feedback & Bug Report

The app lets you send us feedback, a rating, or a diagnostic log file to help solve technical issues. **Nothing is ever sent automatically** — a report leaves your device only when you press the send button. An email field is optionally available if you would like a response from us. The log file contains basic technical information, and sensitive details that are not required for debugging, such as your IP address, are automatically removed before sending.

## Changes to This Privacy Policy

We may update this policy from time to time; the current version is always available on this page, and changes take effect when posted here. Substantive changes are also visible in the [VpnHood repository history](https://github.com/vpnhood/VpnHood), where this policy is maintained.

## Contact Us

Questions about this policy can be raised on our [GitHub repository](https://github.com/vpnhood/VpnHood/issues).
