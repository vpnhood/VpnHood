# Connection Options in a VpnHood App

A VpnHood app can offer a person several ways to connect to a server location: free, free for a rewarded ad, a premium
trial, premium for a rewarded ad, or premium bought or unlocked with a code. This page says which of them a person gets,
what each one costs them, and when the app asks them to choose. [How Ads Work in a VpnHood App](ads.md) says how the
ads work, and [What Happens on Connect](connect.md) gives the technical side.

## What decides the options

- **The person's country.** An access key carries client policies: each for some countries, and one marked `*` for
  every other country. The person's country, by their IP address, picks one, and it says which options are offered
  and how long each lasts. With no policy, a free location connects with no time limit. In VpnHood MANAGER, they are
  the Policies of an Access Key Profile.
- **The location.** A location is free, premium, or both, by the tags on its servers. The free options show only where
  there are free servers, and a trial or premium for an ad only where there are premium ones. A policy can also name
  the locations its free connections may use.
- **The app.** An option with a rewarded ad shows only in an app that can show rewarded ads. Buying premium shows only
  in an app that sells it, in its store or in the operator's own shop, and a code only in an app that accepts codes.
- **A premium key.** A person whose access key is premium gets no choice: the app connects at once, with no ad, no
  trial and no question.

## The options

Each option has its name in the client policy and in the app's API (`ConnectPlanId`):

| Option | What the person gets | What it costs them |
| --- | --- | --- |
| **Free** (`Normal`) | A free connection to a free location. | Nothing, or an ordinary ad where the server asks. |
| **Free for an ad** (`NormalByRewardedAd`) | The same free connection, usually for longer. | A rewarded ad. |
| **Premium trial** (`PremiumByTrial`) | Premium for a while, within a daily limit if the policy sets one. | Nothing. |
| **Premium for an ad** (`PremiumByRewardedAd`) | Premium for a while. | A rewarded ad. |
| **Buy premium** (`PremiumByPurchase`) | Premium, bought. | A purchase, in the app's store or the operator's shop. |
| **Premium code** (`PremiumByCode`) | Premium, with an access code. | A code, from a reseller for example. |

The first four options last the number of minutes their policy gives:

- **A number:** the connection lasts that many minutes.
- **0:** it has no time limit of its own, only the access key's end.
- **No value:** the option is not offered. Without Free, the country's people connect only in the other ways.

The app shows these minutes. The server's side, VpnHood MANAGER or your own access manager, holds the real time and
ends the session when it is up.

During a connection that has a time limit, the person can watch a rewarded ad for more time, where the server's side
allows it ([More time for an ad](ads.md#more-time-for-an-ad)). In VpnHood MANAGER, the ad adds as much free time again
to a free connection that an ad started, and premium to a plain free one, where the policy offers premium for an ad.

## When the app asks

When a location offers a trial or a rewarded ad, free or premium, the app asks the person which way to connect, on a
page that lists the location's options. Otherwise it connects at once with the free connection, and buying premium or
entering a code stays in the app's menus.

A rewarded ad is always the person's choice there, as ad networks require: keep the free connection on wherever a
rewarded ad is offered ([A rewarded ad is the person's choice](ads.md#a-rewarded-ad-is-the-persons-choice)).

## Where the app shows premium

An app either **sells premium**, with a premium plan of its own, as VpnHood! CONNECT does, or **sells none**, as
VpnHood! CLIENT does, where people add access keys they got from anyone.

- **An app that sells premium** marks it wherever it applies: the crown on the session's countdown, the gold connection
  circle, "You are premium", "Go premium", and the crown on premium settings and premium locations. Premium there is
  the app's own plan.
- **An app that sells none shows no premium mark**, with one exception below. Its keys come from different companies
  and people, and the app must not make them compete: any key's owner can mark the key premium, even all of it, and a
  premium mark on the home screen would tell the person it is a better connection when it may be a poor server.
- **The exception: a key's own location list.** When a key has both free and premium locations, the list shows them
  apart, with the crown on the premium ones. There the mark compares one owner's locations with each other, which is
  meaningful. It also warns ahead: a free public key's premium location needs a rewarded ad to connect, which such an
  app does not show, so the connect would fail.

## See also

- [How Ads Work in a VpnHood App](ads.md) — the ads that free connections and rewarded options rely on
- [What Happens on Connect](connect.md) — the steps of a connect, and the errors it can end with
