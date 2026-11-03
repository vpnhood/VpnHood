# Galaxy Store — the steps only you can do

Samsung's Galaxy Store is the store preinstalled on Samsung phones and tablets. Everything on this
page happens in a browser, by hand, in **your** Samsung account. The pipeline builds the Galaxy Store
APK and attaches it to each GitHub release; uploading it to the store is yours.

The Galaxy Store build is its own head, `<Product>.Android.Samsung` (today CLIENT's,
`src/Apps/Client/Client.Android.Samsung`). A Connect-shaped app copies it. Galaxy Store reaches
Samsung's Android devices only: Samsung TVs run Tizen, not Android.

Work through it in order. Come back to [start here](README.md) when you are done.

---

## 1. Open the seller account

1. **A Samsung account.** It is your only way into Seller Portal, so open it under an address your
   company keeps, not a person's. Use your company's own mail domain: Samsung asks why when you apply
   with a public one such as Gmail. *Sign in with Google* makes the account from a Google account's name and
   address, with no password of its own. Its **country** becomes your Seller Portal country, and cannot be
   changed later without deleting the seller account and starting again.
2. **Seller Portal** (`seller.samsungapps.com`) → *Sign Up Now*, signed in with that account. Choose
   **Corporate Seller**, to sell under your company's name. The form asks for the company's name, address
   and phone, its **representative**, and a **primary contact**: the person who runs the account, whom
   Samsung writes to. The address fields reject `#`: write a mailbox or suite number as `PMB` or `Ste`.
   Samsung then emails a link that completes the sign-up. The name you enter must match the government
   ID you submit exactly. The profile starts subscribed to *News, Benefits, Newsletter*: untick it under
   *Profile* → *Edit*, which first asks Samsung to confirm it is you (a one-time email code, for an
   account made with Google).
3. **Commercial seller status**: *Request Commercial Seller* on your profile. It is required for
   free apps too. A corporate seller with an Android app must verify a **D‑U‑N‑S number**, the same
   one Apple asks for: Samsung then copies the company's name, representative and address from that
   record, read-only. Once you send the request, nothing on it changes except through Samsung's
   Support, so check it whole first. Approval takes several days.
4. **Financial information**, on the same request: PayPal or a bank account in your company's name,
   in the same country as the seller account. Samsung pays by international wire, so a bank account
   takes its SWIFT code and account number, not a US routing number. With D‑U‑N‑S, the request needs
   no document uploads. Samsung answers by email in about four business days.

Samsung charges no registration fee.

**What becomes public.** Every app page shows its seller's name, company name, **representative's
name**, phone number, address, privacy policy URL and support email. The representative is the
company's legal contact, and with a D‑U‑N‑S number Samsung takes it from that record's Key Principal:
choose that person, and that phone number and address, knowing they will be published. The primary
contact is a different field, and is not in that list.

---

## 2. Choose the package name and the key

Before the first upload, decide whether the Galaxy Store build is the **same app** as your Google Play
build or a separate one. It cannot be changed after the first release.

| | Same package name as Play | Its own package name |
| --- | --- | --- |
| What a user sees | one app: either store's update installs over the other's | two apps, side by side |
| Needs | the Galaxy Store APK signed with the **same certificate** Play installs carry | nothing more |

The same package name works only when Play signs your app with **your own** key: an app created with
your key before Play App Signing, or one whose key you gave Google. A new app's key is Google's
([Google Play, step 3](google-play.md#3-let-google-hold-your-signing-key)), and then a Galaxy Store
APK signed with your upload key fails every cross-store update with an error. In that case give the
Samsung head a package name of its own: add `<VhAppIdChannel>samsung</VhAppIdChannel>` to its project,
which makes its id `<base>.android.samsung`. Samsung itself recommends a separate package name.

The publish script signs the Galaxy Store APK with the Google Play build's key.

---

## 3. Register the app

Seller Portal → *Add New Application* → *Android*. Upload the APK from your GitHub release,
`<title>-android-samsung.apk`. It holds arm64 and 32-bit arm, for old phones; it leaves out x64,
since every Samsung phone and tablet is ARM. It targets a recent Android, well above Samsung's minimum
(target API 33, and a 64-bit binary, in its latest notice).

---

## 4. Fill in the listing and the review notes

Samsung's [distribution guide](https://developer.samsung.com/galaxy/distribute/distribution-guide)
is what the review checks. The rules a VPN app meets most often:

- **A privacy policy** (3.1.2): its URL in Seller Portal, and shown in the app, which the first
  run's agreement screen does. That is why the Galaxy Store build keeps it on, unlike the Play build.
- **A way in for the reviewer** (1.1.5): an app that needs an account or a key to work needs test
  credentials in the review notes. Give the reviewer an access key to a server of yours.
- **English** (1.3.2): the default language when the app is offered in more than one country.
- **No other stores** (1.3.7): the description and screenshots name no other app store.
- **Local law** (3.4.1): Samsung removes an app from a country whose law forbids it; VPN apps have
  been removed this way.

---

## 5. Every release

The pipeline attaches two files to each GitHub release: `<title>-android-samsung.apk`, which you
upload to Seller Portal as the app's new binary, and `<title>-android-samsung.json`, the update feed
the app reads.

Samsung's review takes days. The feed tells the app of the new version only **seven days** after the
release, so the update notice does not open a store page that has no update yet: upload within those
days.

---

## 6. What the Galaxy Store build does differently

Galaxy Store allows no update from outside the store (1.1.8) and no download of other APKs (3.2.2):

- It has **no in-app updater**. Its update notice's *Update* opens the app's Galaxy Store page, and
  Galaxy Store installs the update itself.
- It has **no store review prompt**: Google Play's is Google's alone.
- Its first run shows **the agreement screen**, as the website build's does: Galaxy Store's install
  takes no acceptance of your terms.
