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
     to OneDrive"**, on by default: the app's data holds the access keys people add.
3. **Age ratings:** if the app already has an IARC certificate — Google Play shows its ID under **App
   content → Content ratings** — Partner Center requires it: **I have already completed the
   questionnaire for this app elsewhere**, the ID, **Search**, then **Save**. Otherwise, answer the
   questionnaire honestly.
4. **Packages:** drag in the `.msix`, then **Save**. Validation warns that `networkingVpnProvider`
   needs approval: expected, as the app's tunnel is a Windows VPN plug-in, which needs it.
   - **A new package of the same version:** remove the old one and **Save** before you upload it. A
     draft takes no two packages of one version with different contents ("must be uniquely identified
     by their full names"), and one marked for removal counts until the page is saved.
5. **Submission options:** **Don't publish this submission until I select Publish now**, so a
   submission that passes certification waits for you. The page also asks, required, why the app
   needs `networkingVpnProvider`.
