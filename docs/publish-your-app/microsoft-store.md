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
name. Copy them into your app's `Directory.Build.props` (`src/apps/<your app>/`):

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
run the Store head's script from your committed code (for the Connect app,
`src/apps/Connect/Connect.Windows.Microsoft`):

    ./_publish.ps1 -store

It writes `bin/Publish-microsoft-store/<name>.msix`: the Release build, under the Store's identity,
with a version ending in `.0` (the Store keeps the last part for itself), and **unsigned** — Partner
Center signs it. Its logos are drawn from the app's icon, `app.ico`, whose 256-pixel picture must be
a PNG.

**Try it as the testers will** before you submit. The same script without `-store` writes
`bin/Publish-microsoft/<name>.msix`, signed with a development certificate, `dev-certificate.cer`,
beside it. On a clean Windows 11 PC or virtual machine, trust that certificate (Local Machine ›
Trusted People), install the package, and walk your notes for certification (step 5) against your
production servers, the code included. Remove it afterwards.

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
     Services Act requires. They must work. Because the app asks for `networkingVpnProvider`, also
     choose **Use different details for this app** and enter your support email and website there,
     even if they are the account's. That is the field Microsoft's certification team named when it
     refused this project's CLIENT app for lacking "a valid support contact or a developer website
     URL", while the account's details already held both (step 7).
   - **Product declarations:** untick **"Windows can include this product's data in automatic backups
     to OneDrive"**, on by default: the app's data holds its access keys. If the app sells premium,
     tick **"This product allows users to make purchases, but does not use the Microsoft Store
     commerce system"** (step 6).
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
   edition: it has no per-app split tunneling. An app that works only with something it does not
   include, such as a VPN server and its access key, says so at the start of its description (10.2.4).
   - **Show the edition's own offer.** The Store head connects with the key it embeds
     (`access_key_premium.txt` in your private settings folder) and shows no ads, so locations that
     need an ad are not free here. With a key whose locations are all premium, as this project's
     Connect's, the Store edition is premium-only: neither the description nor a screenshot may
     promise free locations (10.1). Draw its screenshots from a premium-only location list, as the
     App Store's are: a platform's `patchFile` in the
     [store screenshots](../../tools/StoreScreenshots/README.md) tool.
   - **A screenshot is kept once uploaded**, before **Save** and through a reload: remove a stray one
     with its tile's delete icon.
6. **Submission options:** **Don't publish this submission until I select Publish now**, so a
   submission that passes certification waits for you. The page also asks, required, why the app
   needs `networkingVpnProvider`, in at most 500 characters: who publishes it, with your website and
   support email; what the plug-in does with it; and that in an AppContainer it is the app's only way
   to provide a VPN.
   - **No GitHub link.** The reviewers want a working support contact or a developer website in the
     reason itself, and a GitHub link counts as neither. This project's own CLIENT app was refused
     under Store Policies 10.6.3 for a reason whose only link was its GitHub repository, with a
     listing that already showed the account's website and support email. The reason alone is not
     enough either: rewritten to name the company, the website and the support email, it was
     refused again in the same words. Set the app's own support info too (step 2).
   - **Asked once per account.** After Microsoft approved the capability for this project's CLIENT,
     its Connect showed neither the Packages page's warning nor this box. Explain the capability in
     the notes for certification all the same (step 7).
7. **Additional Testing Information** (under **Supplemental info**): the notes for certification —
   how a tester gets premium and connects: **Go Premium**, **I have a Premium Code**, the code,
   **Activate** — and a premium code from your billing portal under **Credentials**, never in the
   notes, which Partner Center asks to hold none. A stored credential shows masked from then on. If
   your access policy grants a trial, say so: a tester who skips the code sees it.
   Open the notes with who publishes the app (the company, its address, website and support email,
   the privacy policy and terms, the app's pages on other stores) and why it needs
   `networkingVpnProvider`. With that and the app's own support info (step 2), this project's CLIENT
   passed on its third try, the same package that was refused twice.

Then **Submit for certification** on the product's page.

---

## 6. Purchases

Skip if your app sells nothing.

Microsoft lets a non-game app on PCs sell digital goods and subscriptions through its own payment
system as well as through the Store's (Store Policies 10.8.1 and 10.8.6), and takes no share of what it
sells that way. The Store edition does not use the Store's commerce system, so it has no add-ons in
Partner Center: it sells as the website edition does, through your billing portal.

- **Two ways in:** **Purchase via Web** opens your portal's checkout in the browser, after the person
  signs in; **I have a Premium Code** takes a code your portal issued. The code is also what the
  testers get (step 5).
- **Declare it** in Properties (step 5).
- **The checkout carries the policy's conditions:** it names the payment provider, signs the person
  in, has them confirm the purchase, and handles cards to PCI DSS.
- **No payout account or tax profile** in Partner Center for this: those serve sales through the
  Store, and a free app with its own checkout makes none.
- **The trial** your access policy grants shows here as in the other editions
  ([connection options](../connection-options.md)); there is no ad-paid free tier, as the Windows
  edition shows no ads (step 5, Store listings).

---

## 7. The first release

1. **Certification** usually takes a few hours and at most three business days; the restricted
   capability's reason is read then, and a refusal names the policy it failed. Its report is on the
   product's page (**View report**). The draft stays: fix what the report names and **Resubmit for
   certification**; a fix outside the package needs no new build. A refused capability is reconsidered
   only on new or expanded information in its reason: the same text is refused again.
   A resubmission with a restricted capability can sit in certification well past the three days:
   VpnHood! CLIENT's did. Partner Center sends no mail about it, so check the product's page. To ask,
   open a support request: the product page's **Contact Support** goes to Microsoft's support site.
   There, choose **Windows Developer Center** › **App Management**, and in the request name the product
   and its Store ID, say when it was resubmitted, and give the earlier report's id. The free
   **Professional No Charge** plan is enough. It offers only the lowest severity and answers by email
   during business hours.
   For a refusal, write to `reportapp@microsoft.com`, the address the product's page gives for
   certification questions, and name the app's Store ID and your Seller ID (**Account settings** ›
   **Legal info**), or the first reply only asks for them. CLIENT's answer came three business days
   later and named the field the reviewers check (step 2, Support info). Its support request was
   answered after two weeks, when the app had already passed.
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
