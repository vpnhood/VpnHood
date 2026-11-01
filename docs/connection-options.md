# Connection Options & Plans

This document explains, in plain terms, the connection options a user can be offered
for a server location and what each one means. It is meant as a product/behaviour
overview — not an implementation guide.

## The idea

A server location can be offered to a user in several ways. Each way is a **connection
option**. Which options a user sees depends on three things:

- **The user's country** — policies are defined per country (with a default `*` fallback).
- **The location type** — a location can be *free*, *premium*, or both.
- **Whether the user is already premium** — premium users skip all the choices below and
  simply connect (no ads, no trials, no prompt).

When a location offers more than one option, the app **prompts** the user to choose. When
there is only a single option, the app just connects with it.

## The options

| Option | What the user gets | Costs the user |
|--------|--------------------|----------------|
| **Normal** | A standard free connection to a free server. | Nothing. |
| **NormalByRewardedAd** | The same free connection as *Normal*, but usually for a **longer** free period. | Watch a rewarded ad first. |
| **PremiumByTrial** | A temporary premium connection, offered as a free trial. | Nothing (time-limited). |
| **PremiumByRewardedAd** | A temporary premium connection. | Watch a rewarded ad first. |
| **PremiumByPurchase** | A full premium connection. | Buy it (in-app or via a purchase link). |
| **PremiumByCode** | A full premium connection. | Enter an access code. |

### About the numbers

Some options carry a **number** (for example *Normal*, *NormalByRewardedAd*,
*PremiumByTrial*, *PremiumByRewardedAd*). Think of it as a hint about the reward — typically
**how long the granted session lasts**. The exact rule is decided and enforced by the
server, so the number is mainly a hint the app can show the user.

For the *Normal* option specifically:

- **No value at all** → the free option is **not** available here (it is a premium-only
  location for this user).
- **Zero** → the free option is available with the standard/default behaviour. Premium users
  always fall into this case.
- **A positive number** → the free option is available, and the number is the hint described
  above.

### NormalByRewardedAd in particular

*NormalByRewardedAd* is the free sibling of *Normal*. It is the same kind of free connection,
but the user watches a rewarded ad to unlock it — usually in exchange for a longer free
session than plain *Normal*. It is offered only when:

- the location has a free tier (just like *Normal*), **and**
- the policy enables it, **and**
- the app is able to show rewarded ads.

If it is not enabled, the option simply does not appear.

## How premium users are handled

If a user is already premium, none of the options above are offered. The app treats every
location as a normal connection and connects directly — premium users never see ads, trials,
or the option prompt.

## Where the app shows premium

An app either **sells premium** — it has its own premium plan, as VpnHood! CONNECT does — or it
**sells none**, as VpnHood! CLIENT does: there people add access keys they got from anyone.

- **An app that sells premium** marks it wherever it applies: the crown on the session countdown,
  the gold connection circle, "You are premium", "Go premium", and the crown on premium settings
  and premium locations. Premium there is the app's own plan.
- **An app that sells none shows no premium mark**, with one exception below. Its keys come from
  different companies and people, and the app must not make them compete: any key's owner can
  mark the key premium, even all of it, and a premium mark on the home screen would tell the user
  it is a better connection when it may be a poor server.
- **The exception: a key's own location list.** When a key has both free and premium locations,
  the list shows them apart, with the crown on the premium ones. There the mark compares one
  owner's locations with each other, which is meaningful. It also warns ahead: a free public key's
  premium location needs a rewarded ad to connect, which such an app does not show, so the connect
  would fail.
