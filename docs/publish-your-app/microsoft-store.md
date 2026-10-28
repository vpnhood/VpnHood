# Microsoft Store — the steps only you can do

Everything on this page happens in a browser, by hand, in **your** Microsoft Partner Center account,
except building the package. The Store edition of the Windows app is an MSIX package with a VPN
plug-in; this page takes it from an account to a first submission.

Work through it in order. Come back to [start here](README.md) when you are done.

---

## 1. Open the developer account

Register at **Microsoft Partner Center** for apps and games, as a **company**. Microsoft verifies the
company — its legal name, address and usually a D‑U‑N‑S number — before you can publish. The name you
register is the publisher name the Store shows under your app.

---

## 2. Reserve the app's name

**Apps and games → New product → MSIX or PWA app**, then reserve your app's name exactly as the app
shows it (`VhAppName`): the Store refuses a package whose display name is not one of the product's
reserved names.

- **Submit within three months.** A reservation with no submission by then is released.
- **Not "EXE or MSI app".** That type is for an installer the Store links to; the Store edition is an
  MSIX, which only an "MSIX or PWA app" product takes.

---

## 3. Put the product's identity into your project

**Product management → Product identity** lists three values Microsoft assigned when you reserved the
name. Copy them into your app's `Directory.Build.props` (`src/Apps/<your app>/`):

| Partner Center | Property |
| --- | --- |
| `Package/Identity/Name` | `VhAppMsixIdentityName` |
| `Package/Identity/Publisher` | `VhAppMsixPublisher` |
| `Package/Properties/PublisherDisplayName` | `VhAppMsixPublisherDisplayName` |

You cannot choose them, and the Store rejects a package under any other identity. They do not replace
your app's own identifier (`VhAppIdBase`), which stays what it is on every platform.

---

## 4. Build the upload package

The pipeline does not build the Store package yet. On Windows, with the .NET SDK and the Windows SDK,
run the Store head's script from your committed code (for the Client app,
`src/Apps/Client/Client.Windows.Microsoft`):

    ./_publish.ps1 -store

It writes `bin/Publish-microsoft-store/<name>.msix`: the Release build, under the Store's identity,
with a version ending in `.0` (the Store keeps the last part for itself), and **unsigned** — Partner
Center signs it. Its logos are drawn from the app's icon, `app.ico`, whose 256-pixel picture must be
a PNG.

---

## 5. Fill in the submission

**Start submission** on the product's page opens a draft; nothing reaches Microsoft's review until
**Submit for certification**.

1. **Pricing and availability:** a base price of 0, and **Restrict to specific markets** with every
   market ticked but those a VPN must not be sold in — the reasoning is the same as for Apple:
   [territories](../legal/developer/APP_STORE_TERRITORIES.md). The market list is a dialog: tick,
   then **OK**, then **Save draft**.
2. **Properties:**
   - **Category:** Security → Personal security.
   - **Personal information:** **Yes** — the app sends your server the person's Client ID, the server
     sees their IP address, and analytics run where they are on — with your privacy policy's URL.
   - **Support info:** your account's contact details, which the listing shows as the EU Digital
     Services Act requires. They must work.
   - **Product declarations:** untick **"Windows can include this product's data in automatic backups
     to OneDrive"**, on by default: the app's data holds the access keys people add. For Connect, tick
     **"This product allows users to make purchases, but does not use the Microsoft Store commerce
     system"** (step 6).
3. **Age ratings:** if the app already has an IARC certificate — Google Play shows its ID under **App
   content → Content ratings** — Partner Center requires it: **I have already completed the
   questionnaire for this app elsewhere**, the ID, **Search**, then **Save**. Otherwise, answer the
   questionnaire honestly.
4. **Packages:** drag in the `.msix`, then **Save**. Validation warns that `networkingVpnProvider`
   needs approval: expected, as the app's tunnel is a Windows VPN plug-in, which needs it.
   - **A new package of the same version:** remove the old one and **Save** before you upload it. A
     draft takes no two packages of one version with different contents ("must be uniquely identified
     by their full names"), and one marked for removal counts until the page is saved.
5. **Store listings:** one listing per language the package declares (the Store head declares
   English); **Manage additional languages** adds others. Microsoft wants the description in every
   language the package declares (Store Policies 10.7). Each needs a description and at least one
   **Desktop** screenshot (PNG, 1366×768 or larger); a short description and up to 7 keywords help
   search — no prices or other publishers' product names among them (10.1.3). Describe the Windows
   edition: it has no per-app split tunneling.
   - **A screenshot is kept once uploaded**, before **Save** and through a reload: remove a stray one
     with its tile's delete icon.
6. **Submission options:** **Don't publish this submission until I select Publish now**, so a
   submission that passes certification waits for you. The page also asks, required, why the app
   needs `networkingVpnProvider`, in at most 500 characters: what the plug-in does with it, and that
   in an AppContainer it is the app's only way to provide a VPN.
7. **Additional Testing Information** (under **Supplemental info**): the notes for certification —
   how a tester adds the key and connects — and the key itself under **Credentials**, never in the
   notes, which Partner Center asks to hold none. A stored credential shows masked from then on.

Then **Submit for certification** on the product's page.

---

## 6. Purchases

Skip if your app sells nothing.

Microsoft lets a non-game app on PCs sell digital goods and subscriptions through its own payment
system as well as through the Store's (Store Policies 10.8.1 and 10.8.6). So Connect's Store edition
sells as its website edition does: a premium code, or the portal's checkout in the browser, after the
person signs in. Partner Center wants it declared (step 5, Properties), and the policy's conditions
fall on the checkout: it must name the payment provider, sign the person in, have them confirm, and
handle cards to PCI DSS.

---

## 7. The first release

1. **Certification** usually takes a few hours and at most three business days; the restricted
   capability's reason is read then, and a refusal names the policy it failed.
2. **The publish waits for you** (step 5, Submission options): once certified, **Publish now** on the
   product's page releases it.
3. **Usage data asks first.** Microsoft wants the person's opt-in, in the app, before personal
   information reaches a third party (Store Policies 10.5.2), which the analytics' data is: the Store
   heads ask on the first-run page, box unticked, in every region (`IsAnonymousTrackerOptInRequired`).

---

## 8. See the Store's update and rating dialog before a release

Optional. The app asks the Microsoft Store whether it has an update, and shows the Store's own update
and rating screens. The Store answers only an app it installed: a package you sideload, as
`_install.ps1` does, gets "the Store does not know this app" (0x803F6107) from every call, which the
app takes as no update. To watch them:

1. Release a version hidden from customers: in **Pricing and availability**, **Visibility**, make the
   product discoverable only by its direct link, and **Publish now** once it is certified.
2. Install it from the Store through that link, signed in with your Microsoft account, and run it
   once.
3. Release a second submission with a higher version. Open the app: the Store offers the second
   version, and the app shows the Store's update screen. A top rating in the app's own rating dialog
   then brings up the Store's.

---

## 9. Afterwards

Each release is a new submission with a higher version, its last part still 0 (step 4); a submission
that changes only the package keeps the rest. The pipeline does not publish to the Microsoft Store
yet.
