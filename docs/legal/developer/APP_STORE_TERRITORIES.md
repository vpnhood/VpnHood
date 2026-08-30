# App Store Territories — where NOT to sell a VPN

Which App Store territories a VPN fork should deselect before its first release, and why. Written
for forkers; companion to [APP_STORE_PRIVACY.md](APP_STORE_PRIVACY.md) and
[APP_STORE_EXPORT_COMPLIANCE.md](APP_STORE_EXPORT_COMPLIANCE.md). Not legal advice; a fork makes
its own availability decisions under its own name.

## TL;DR

In App Store Connect → your app → **Pricing and Availability → Availability**, deselect at least:

| Territory | Why | Risk falls on |
| --- | --- | --- |
| **China mainland** | Apple requires a Chinese MIIT license for VPN apps and has removed unlicensed ones since 2017. A fork will never hold that license. | You (removal, account record) |
| **Russia** | Apple removes VPN apps from the Russia storefront on Roskomnadzor demands (ongoing since 2024; well over a hundred removed). | You (removal, account record) |
| **UAE** | VPN apps are not purged from the store, but local law penalizes using a "fraudulent IP address" to commit an offence, and the state blocks VPN provider sites. | Mostly your users |

Do it **before the first release** — see [Deselect early, not late](#deselect-early-not-late).

## The reasoning, per territory

**China mainland.** Since 2017 Apple has required VPN apps on the China storefront to hold a
government (MIIT) license and removed the hundreds that did not. The license is only issued to
state-approved providers; an open-source fork cannot obtain one. Leaving China selected therefore
has exactly one outcome: at some point Apple removes the app from that storefront and the removal
lands as a notice on your developer account. Deselecting yourself produces the same availability
with none of the record.

**Russia.** Since mid-2024 Apple has been removing VPN apps from the Russia storefront at
Roskomnadzor's demand under the 2017 VPN law (providers must filter the state blocklist to stay
legal — which defeats the app's purpose). As with China, the choice is not *whether* the app is
available there, it is *who* removes it and what paper trail that creates.

**UAE.** A judgment call, decided differently: Apple does not purge VPNs from the UAE storefront,
and VPN use for legitimate purposes is lawful there. But the cybercrime law (Federal Decree-Law
34/2021) penalizes using a fraudulent IP address in the course of an offence — including accessing
blocked services such as unlicensed VoIP — and the telecom regulator blocks VPN providers'
websites. The exposure here is mostly the *user's*, plus reputational and regulatory pressure on
the provider. Deselecting costs a fork essentially nothing and removes the ambiguity.

## Your subscriptions have their own list

Deselecting a territory on the app does **not** deselect it on your subscriptions. Each product
carries its own *Availability* list, it defaults to every territory, and App Store Connect never
warns that the two disagree.

Nothing breaks while they disagree — a subscription cannot be bought in a storefront that does not
offer the app — which is exactly why it goes unnoticed. The cost arrives later: whoever re-enables a
territory on the app re-enables selling there too, silently, without ever making that second
decision. Set both lists the same at the first release, while the products have never been sold and
changing them is free.

Do the same for the **subscription group**? There is nothing to do — a group has no availability of
its own; it follows its products.

Both the app and each subscription also carry **"make available in new territories"**, on by
default: any storefront Apple opens in future is enabled automatically. Leaving it on means this
page's decision is revisited for you, by Apple, without notice.

## What deselecting does NOT do

- It is an **availability choice, not a legal shield**. Users with a foreign Apple ID, a sideload,
  or a shared access key can still run the app from those countries; your servers will still see
  their traffic. Nothing here changes what your service does — only where the store offers it.
- It does not satisfy **sanctions law**. U.S. OFAC-embargoed regions (Iran, North Korea, Syria,
  Cuba, Crimea) are a separate, harder obligation; Apple's store does not even operate in most of
  them, but do not treat storefront absence as compliance.
- It does not protect the **listing content**. A store description promising to "bypass government
  censorship" invites scrutiny everywhere; keep the listing about privacy and security.

## Deselect early, not late

Removing a territory where the app is already live is worse than never listing there: existing
users keep the installed app but **lose updates**, including security fixes — the outcome a VPN
should avoid most. Set availability on the very first release, when the choice is free.

Other territories worth a look before launch, same logic at lower intensity: Belarus and
Turkmenistan (Russia-style state hostility to VPNs), Oman (licensing regime like the UAE's), and
any storefront where a fork's operator has personal legal exposure. Google Play has the same
country-availability control; apply the same reasoning there.

## Keep in sync

Revisit this list when a government newly orders VPN removals from a storefront (the China and
Russia entries both started as news events), or when Apple adds a licensing requirement for a
territory, as it did for China in 2017.
