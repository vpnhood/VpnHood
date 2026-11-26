# How Ads Work in a VpnHood App

A VpnHood app can pay for its free servers with ads. This page follows one person from a tap on Connect to more time
won by an ad, and says who decides each step: the server's side decides when a session needs an ad and what the ad
earns, and the app decides which ads it shows and how. It is written for the people who run the business;
[What Happens on Connect](connect.md) gives the technical side.

## The server decides when an ad is needed

When the person taps Connect, the app asks the server for a session, with the choice the person made: an ordinary
connection, a free one that lasts longer, premium for a while, or a premium trial. The server asks its access manager,
the service that holds the access keys and their rules: VpnHood MANAGER, or one of your own. Its answer says what the
session needs first:

- **No ad.** The VPN connects at once. A premium user never sees an ad.
- **An ordinary ad.** The app shows one when it can. When none can show, the person still connects.
- **A rewarded ad.** The person chose to watch an ad for what it gives: premium for a while, or longer on the free
  servers. The ad must be watched to the end and accepted by the server, or the person does not connect, as they did
  not earn it.

These rules live on the server's side, so they change without a new release of the app. In VpnHood MANAGER, an access
key's settings say whether its ordinary connections show an ad, and the client policies say, country by country, which
choices the person gets and how long each lasts ([Connection Options & Plans](connection-options.md)).

## What the person sees

1. They tap Connect, and the app finds a server and starts the session.
2. When the session needs an ad, the app shows Loading Ad, then the ad, full screen.
3. When the ad closes, the app shows Connected. An ad that showed before the VPN started has a few more seconds: only
   then does the VPN take over the device's traffic, so the ad network's own report of the ad still leaves on the
   person's own network.

## Where the ad loads

Many ad networks refuse traffic that comes from a VPN, and each picks its ad for the country it sees. So the app tries
the ad in up to three ways, in this order, and stops at the first that shows:

1. **Before the VPN.** The ad loads on the person's own network, before the VPN starts, so the ad network sees where the
   person really is, and no VPN.
2. **Beside the VPN.** When that fails, the VPN starts, and while the app tries again, the device's new connections go
   around the VPN, on the person's own network. When the try ends, they close and come back through the VPN.
3. **Through the VPN.** Last, the ad loads through the VPN, like the rest of the device's traffic. Only the ad networks
   that the app marks as serving VPN users take part ([Ads through the VPN](#ads-through-the-vpn) says what that
   risks).

In each try, the app's networks take turns in the order the app lists them, and a network with no ad to show passes
its turn to the next. On the last try, an app can show an ad of its own, such as a screen that offers its premium plan.
It always shows, so it comes only after every paid network has had its turn.

## Ads through the VPN

Through the VPN, every person's ad reaches the ad network from your VPN server's address: one address in a data
center, shared by everyone on that server. That has two costs, so the app tries this way last, and only with the
networks it marks.

- **Fewer and poorer ads.** The network sees your server, not the person: the server's country, not theirs, and a data
  center, not a home or a phone network. It has fewer ads to offer there, and pays less for each.
- **Your ad account at risk.** Many people's views and clicks come from the same few addresses, your servers'. To an
  ad network that is invalid traffic, as if you were clicking your own ads, and it can hold back your earnings, or
  suspend or close your account.

Mark a network as serving VPN users only when its policy, or its support in writing, accepts ads loaded through a VPN.

## More time for an ad

A session can have a time limit, which the app counts down. Where the server's side allows it, the person can watch a
rewarded ad during the session for more time. The app offers it only where one of its networks for rewarded ads serves
the person's country. The ad loads beside the VPN first, then through it.

The server's side decides how much time an ad adds. If it does not accept the ad, the session goes on with the time it
had.

## How the server checks a rewarded ad

When a rewarded ad ends, the app sends the server the ad's proof, and the server asks its access manager whether the
ad counts. The app's word alone is not enough: VpnHood MANAGER grants the reward only once the ad network itself has
said the ad was watched. If the access manager refuses the ad of a connect, the person does not connect; if it refuses
the ad of a session's extension, the session keeps the time it had.

## When no ad can show

- **No network has an ad, or none serves the person's country.** An ordinary ad is skipped, or the app's own ad shows,
  and the person connects. A rewarded ad fails, and so does the connect, as the person did not earn what it gives.
- **An ad blocker.** An app can refuse to go on when an ad fails while the device's private DNS, a common way to block
  ads, names a provider the app has not allowed. The person sees why. This is off unless the app turns it on.
- **An app with no ad networks.** It does not connect to a server that needs an ad.

## Adding an ad network

An app can show ads from several networks, and each one more fills ads the others cannot. VpnHood has packages for
Google AdMob, [Chartboost](https://github.com/vpnhood/VpnHood.AppLogic.Ads.Chartboost.Android) and
[InMobi](https://github.com/vpnhood/VpnHood.AppLogic.Ads.InMobi.Android), which a developer adds to the app in a few
lines; for another network, a developer writes a small provider over its SDK, as those packages do.

1. **Check that the network accepts VPN apps.** Many do not, and a network that finds its ads in a VPN app can close
   the account and keep what it earned. Read its publisher policy on VPN, proxy and similar apps before you sign up;
   where the policy says nothing, ask its support, and keep the answer.
2. **Sign up and make the ad units** the app needs. The network gives each an id, which the app is built with.
3. **Add it to the app**, with the countries it shows in and its place in the order.
4. **Add its lines to your app-ads.txt**, the file at the root of the website your store listing names, which tells ad
   buyers that the network may sell your app's ads. Buyers may skip an app whose file does not list it.
5. **Decide whether it loads ads through the VPN**: only where it accepts that traffic
   ([Ads through the VPN](#ads-through-the-vpn)).

### Networks that do not accept VPN apps

Don't try these. Their policies, last reviewed on 2026-11-25, do not allow VPN apps:

- **Unity Ads.** [Unity's content policy](https://unity.com/legal/content-policy) does not allow VPN apps without
  Unity's prior written approval, and [its invalid activity policy](https://unity.com/legal/invalid-activity-policy)
  counts traffic from an unapproved VPN app as invalid.
- **AppLovin.** [Its publisher policies](https://legal.applovin.com/policies-publishers/) do not allow its services in
  apps that provide a VPN, a proxy or a similar service.

Policies change, so read a network's own before you sign up.

## See also

- [What Happens on Connect](connect.md) — the ad's step among the others, and the errors a connect can end with
- [Connection Options & Plans](connection-options.md) — the choices a person is offered, and what each costs them
