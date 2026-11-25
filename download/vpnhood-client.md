# Download VpnHood! CLIENT

> **The official page is on the VpnHood website:** [Download VpnHood! CLIENT](https://www.vpnhood.com/vpnhood-client/download/),
> in 13 languages. This copy is for readers who cannot reach the website.

VpnHood! CLIENT has no servers of its own. It connects with an access key, also called a server key, from whoever runs
the server: you, or someone you trust. [How to get an access key](https://www.vpnhood.com/vpnhood-client/docs/first-key/).

<a href="https://play.google.com/store/apps/details?id=com.vpnhood.client.android"><img src="../docs/images/stores/google-play.webp" width="200" alt="Get it on Google Play"></a>
<a href="https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodClient-android-web-arm64.apk"><img src="../docs/images/stores/github-apk.webp" width="200" alt="Get the APK from GitHub"></a>
<a href="https://apt.izzysoft.de/packages/com.vpnhood.client.android.web"><img src="../docs/images/stores/izzyondroid.webp" width="200" alt="Get it at IzzyOnDroid"></a>
<a href="https://apps.apple.com/app/id6788176050"><img src="../docs/images/stores/app-store.webp" width="200" alt="Download on the App Store"></a>
<a href="https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodClient-win-x64.exe"><img src="../docs/images/stores/windows.webp" width="200" alt="Download for Windows"></a>
<a href="#linux"><img src="../docs/images/stores/linux.webp" width="200" alt="Get it on Linux"></a>

## Android and Android TV

Android 7.0 or later, on phones, tablets and Android TV. [Set it up on your TV](https://www.vpnhood.com/vpnhood-client/docs/tv/).

Get it from Google Play when you can. The APK from GitHub does not use Google Play. It is the ARM64 version, for most
phones; for old phones and many Android TVs, download the
[universal version](https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodClient-android-web.apk).

### IzzyOnDroid

The app is also on [IzzyOnDroid](https://apt.izzysoft.de/packages/com.vpnhood.client.android.web), an F-Droid repository
that others run, not us. It takes the app from our GitHub releases, but we do not run it, so install from it only if you
trust it.

## iPhone, iPad and Apple TV

iOS or iPadOS 18.0 or later, and tvOS 17.0 or later on Apple TV.
[Set it up on your TV](https://www.vpnhood.com/vpnhood-client/docs/tv/).

## Windows

Windows 10 or later, x64. The app needs a processor with SSE4.2 and POPCNT, as every processor of the last ten years
has.

## Linux

Run this command, on x64 or ARM64:

```bash
sudo bash -c 'u=https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodClient-linux.sh; bash <(wget -qO- $u 2>/dev/null || curl -fsSL $u)'
```

It supports Ubuntu 22.04 and later (Kubuntu and Lubuntu too), Debian 13 and Arch Linux; other distributions with systemd
should also work. The website has the rest: [install it on Linux](https://www.vpnhood.com/vpnhood-client/docs/linux/).
