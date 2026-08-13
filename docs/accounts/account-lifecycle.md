# Account lifecycle — business flow

*Last reviewed: 2026-08-13*

The life of a VpnHood! account in business terms: when one comes into existence, what it holds while
it lives, what happens when someone deletes it, and how a paying customer gets back what they bought.
No implementation detail — this is the behaviour a support agent, a store reviewer or a lawyer needs
to be able to predict, and the reference the in-app wording and the privacy policy must agree with.

Applies to **VpnHood! CONNECT**, the only app with sign-in today.

## Contents

1. [The one rule](#1-the-one-rule)
2. [The three things we keep apart](#2-the-three-things-we-keep-apart)
3. [Who has an account at all](#3-who-has-an-account-at-all)
4. [Life of a subscription](#4-life-of-a-subscription)
5. [Deleting an account](#5-deleting-an-account)
6. [What deletion does not do](#6-what-deletion-does-not-do)
7. [Coming back afterwards](#7-coming-back-afterwards)
8. [Situations and answers](#8-situations-and-answers)
9. [Where a person can do it](#9-where-a-person-can-do-it)
10. [What we promise before they confirm](#10-what-we-promise-before-they-confirm)
11. [Open questions](#11-open-questions)
12. [Where the wording lives](#12-where-the-wording-lives)

Section 8 answers these, in order:

- They already have a subscription and try to buy again
- They order a second subscription on our website
- They bought on our website, then sign in to the app
- Their subscription came from a different store than the app
- They want to delete, and their subscription came from a different store
- They want to delete, and they bought on our website
- They have a premium code they typed in themselves
- They are connected when they delete
- They never signed in
- They ask for a refund instead

## The short version

| Question | Answer | Where |
|---|---|---|
| Who has an account? | Only someone who signed in — and the only reason to sign in is to buy | §3 |
| Website buyer signs in — premium? | **Not today** (they must paste the code). Decided: yes, automatically | §8 |
| Which key, if they own several? | The first one they bought; later purchases never steal that slot; several unclaimed → ask, never guess | §8 |
| Two subscriptions? | Any number on the website (for sharing); never more than one from a store | §8 |
| What does deletion erase? | The person, on every device. Premium granted by the account dies with it | §5 |
| What does deletion keep? | Their store subscription, their website keys, and anonymised invoices | §6 |
| What blocks deletion? | **Nothing.** Billing is cancelled at end of period instead (today it still refuses — being changed) | §8 |
| Coming back? | A new, empty account — the store subscription can be reattached | §7 |

---

## 1. The one rule

> **An account exists only while a person is signed in. Premium granted by an account is valid only
> while that account holds it. The subscription that paid for it belongs to the store, not to us.**

Almost every question below is that rule applied to a situation. When a new case comes up, answer it
from the rule first — if the rule gives an unwanted answer, change the rule here, not the case.

## 2. The three things we keep apart

Nearly every question about accounts is really a question about which of these three you mean. They
have different owners and different lifetimes, and conflating any two of them is where the confusion
always comes from.

| | What it is | Who owns it | Survives deletion? |
|---|---|---|---|
| **The person** | The account: the sign-in identity and the email address behind it | Us | **No** — erased |
| **The subscription** | Proof that money was paid, and the promise to keep charging | The store (or our website) | **Yes** — we cannot cancel it |
| **The premium code** | The credential that actually opens premium on a device | Us | **Yes**, on our servers — but it stops working on every device |

The premium code is deliberately **not** personal data: it is a random string that opens a gate. It
carries no name, no email, nothing about who holds it. That is why we can keep it after erasing the
person — and why keeping it is not a privacy compromise.

The subscription is the one thing we do not control. Whoever took the money owns the renewal.

## 3. Who has an account at all

Most people never do. The app works without one.

An account is created the first moment someone signs in — and the only reason to sign in is to buy
a subscription or to bring one back. So:

- Never signed in → **there is nothing to delete**, and the app should not offer it.
- Signed in → exactly **one** account, whichever platform they signed in from.

There is one account per person, not one per device and not one per store. Signing in on a second
device joins the same account.

## 4. Life of a subscription

1. The person signs in. We create their account, or recognise the existing one.
2. They buy through the store on their device. The store takes the money.
3. We check the purchase with that store, and record that this account now holds a subscription.
4. We attach a premium code to the account and the app starts using it.
5. Every device signed in to that account picks up the same premium code, so premium follows the
   person, not the device.

Renewals are the store's business. It tells us the subscription renewed; nothing on the device has
to happen.

## 5. Deleting an account

The person taps **Delete my account** in the app, or does the same from the website client area.
Both do the same thing — there is only one account.

Before anything is erased, we tell them what it means (§10). Then, in this order:

1. **Stop the money first.** Every service bought on our website is cancelled at the end of its paid
   period, so no renewal invoice is ever generated and the key keeps working until the time they
   bought runs out. Unpaid invoices are cancelled; paid ones are kept, and the stored payment method
   is dropped. Nothing here refuses the deletion — see §8.
2. **Erase the person.** Sign-in sessions on every device, the sign-in identity, the email address,
   the account itself.
3. **Cut the account free from its premium code.** The code is kept on our side, but it now belongs
   to nobody.
4. **Anonymise the billing record.** Invoices survive with the amounts and dates intact; the name
   and email address are replaced with placeholders. **Under review** — see §11, question 9: the law
   does not ask for this, and in some countries it is the anonymising itself that is the problem.
5. **Write the journal entry** — numeric ids and the gateway's agreement reference, no personal data
   — so the anonymisation can be re-applied after a backup restore, and so a stray charge can still
   be traced to an agreement someone can cancel.

Billing is stopped first and identity erased last, on purpose: a half-finished deletion must never
leave a live charge behind an account that no longer exists. If any step cannot be completed, the
whole thing aborts with a message rather than half-deleting.

### On the devices

The account is gone, so premium goes with it — **on every device, not just the one that pressed the
button**:

- The device that deleted signs out and drops premium immediately, disconnecting if connected.
- Every other device discovers it on its next contact with our servers — at the latest, its next
  launch — and signs itself out and drops premium then.

There is no push, and no way for us to reach a device that is offline. A device that never runs
again simply never finds out; it holds a credential our servers no longer associate with anyone.

## 6. What deletion does **not** do

- **It does not cancel a subscription.** The store keeps charging until the person cancels it there.
  We say this before they confirm.
- **It does not refund anything.** Refunds are the store's decision for store purchases, and ours
  only for website purchases.
- **It does not erase invoices.** We are legally required to keep financial records. Today the name
  and email on them are replaced with placeholders — whether that is right is under review (§11,
  question 9), and changing it changes the privacy policy and the public deletion page with it.
- **It does not reach into backups instantly.** Residual copies may remain for up to **30 days**,
  after which they expire.
- **It does not erase what other companies hold in their own right** — the sign-in provider, the
  store that billed, the payment processor. They keep their own records under their own policies.

## 7. Coming back afterwards

Signing in again creates a **brand-new, empty account**. The old one is not restored, and we keep no
way to recognise the returning person — that is deliberate.

The subscription, however, is still theirs. It lives at the store, under their store account, and if
it is still running it can be brought back: the app proves ownership to the store, we match that
proof to the premium code we preserved, and attach it to the new account. **The same code comes
back, not a new one** — otherwise every delete-and-return would mint a fresh service.

> **Status: partly built.** The preserved mapping and the re-attachment exist on our side. What is
> missing is the trigger — nothing currently asks the store, at sign-in, whether this person already
> owns a subscription we have not linked yet. Until that lands, a returning person has no in-app
> route back to premium. See §11.

## 8. Situations and answers

### They already have a subscription and try to buy again — including from a different store

**We refuse the purchase.** The subscription belongs to the account, not to the platform, so an
account that already holds one cannot buy a second. The app says they already have an active
subscription and sends them to their account page.

This is what stops someone paying twice by subscribing on a phone and then again on a tablet from a
different store.

### They order a second subscription on our website

**Nothing stops them, and that is on purpose.** The website sells a *code*, not a seat on an
account: a person can buy two, five or fifty, and each one is an independent service with its own
code, its own billing cycle and its own cancel button. Selling many at once is an explicit product
mode for resellers.

The business reason is **sharing**: someone buys several keys and gives them to their family. The
stores cannot do this — a store subscription belongs to one store account — so the website is the
only place it can happen, and the difference between the channels is deliberate, not an oversight.

This is the deliberate opposite of the store rule above, and the difference is what is being sold:

| | Bought in a store | Bought on our website |
|---|---|---|
| What the person gets | Premium on **their account** | A **code**, which they hold and can give away |
| A second purchase | Refused — one account, one subscription | Allowed, any number |
| Who can use it | Whoever signs in to that account | Whoever has the code |
| Cancelling | Only in that store | In the website client area |

So a store subscription is tied to a person, and a website code is a bearer good — closer to a gift
card than to a seat. Someone buying a second website subscription is usually doing it for a family
member, and refusing that would break a normal sale.

The consequence for deletion is that "cancel your website subscription first" means **all** of them.

### They bought on our website, then sign in to the app

**They get an account with no subscription, and the app shows them as not premium.** The two
channels do not meet by themselves.

What actually happens:

1. Signing in **attaches them to their existing website customer record if the email matches** — the
   one the sign-in provider proved. Signing in never creates a customer record on its own; that
   happens only at a first store purchase.
2. The app then asks what the account holds — and that question is answered from the **store
   purchase ledger only**. A website order is an ordinary service, not a store purchase, so nothing
   comes back.
3. Their code is sitting in the website client area. To use it they must **copy it into the app by
   hand**, exactly as if a friend had given it to them.
4. If they signed in with a different address than they bought with, they are simply two unrelated
   customers to us.

So a website purchase behaves like the bearer good it is: the account knows nothing about it, and
the code is what carries the value. That is consistent, but two consequences are worth knowing.

**They can be charged twice.** The "you already have a subscription" refusal looks only at the store
ledger, so a customer who already pays us monthly on the website can be sold a store subscription in
the app without warning. Nothing detects it, and the person ends up paying us twice for the same
thing.

**Their website purchase blocks deletion — even a one-time one.** Any service we did not sell through
a store, active or suspended, refuses the whole deletion. That is being replaced by cancelling the
billing instead; see *They want to delete, and they bought on our website* below.

#### How it should work — decided, not yet built

> **Decided 2026-08-13.** This is the intended behaviour, not a proposal; nothing below is
> implemented yet. What the app does today is described above.

Most website buyers buy **one** key, for themselves. Making that person type a code they have
already paid for is a step with no purpose and a support ticket waiting to happen. So signing in
should make them premium — and the ambiguity only appears when they hold more than one.

The rule that resolves it is *auto-apply when there is nothing to guess, ask when there is*. Only
**usable** codes count — an expired or cancelled one is not a candidate for anything:

| Situation, in order | What the app does |
|---|---|
| The device already runs on a **usable** code | Leave it alone |
| A store subscription | Use it — it belongs to the account and there is only ever one |
| The buyer already chose a key, and it is still usable | Use that one |
| Exactly one usable code | Apply it silently — there is nothing to choose |
| Several usable codes | Sign them in, list the codes, let them pick |
| None | Signed in, not premium — as today |

Read top to bottom; the first row that matches wins. "Several" is the only row that ever asks a
question, and it asks because we must not guess which key is theirs and which is their daughter's.

Three guardrails make the automatic part safe:

1. **Never overwrite a code that still works.** If the device is running on a usable code — typed by
   hand or chosen earlier — signing in leaves it alone. Replacing a working key is the one genuinely
   destructive move here. A code that has **expired** is not protected: it opens nothing, so
   replacing it is a repair, not a loss.
2. **Always changeable.** Whatever is applied must be visible and switchable, so a wrong choice
   costs one tap. That is what makes applying a code automatically an acceptable risk: it consumes
   a device slot, it does not consume the key.
3. **Record it as the buyer's own code, not as one the account granted.** A website key was bought
   outright. If the app filed it as account-granted, deleting the account would confiscate a key the
   person owns — see §5. It must be treated exactly like a code they typed in.

**The choice belongs to the account, not to the device.** Someone with three keys should answer
"which one is mine" once, not on every phone they own. Remembering it against the account is what
makes the picker a one-time event instead of a recurring annoyance.

**The first key bought becomes the account's key, and no later purchase ever takes that over.** This
is what stops a gift from disturbing the buyer: without it, someone happily running on their single
key would be asked "which one is yours?" the moment they bought a second for their daughter — a
question caused entirely by somebody else's present. Two rules keep it stable:

- A purchase claims the slot **only if the account has no key set, or the one it has is no longer
  usable**. The second half is the "mine ran out, I bought another" case, which would otherwise
  leave the account pointing at a dead key.
- When the app applies a key by itself (the "exactly one" row), it **records that as the choice**,
  so the two routes converge and the count going from one to two never turns a settled account into
  a question.

Either way it stays a preference, not a reservation: naming a key as the account's does not lock it,
and it must be changeable from the client area and from the app.

**Nothing is asked at purchase time.** Checkout is the worst place to add a question, the buyer often
does not know yet who a key is for, and the answer can change the next day. Instead the client area
should let them **name** a key afterwards, and the picker falls back to product name, expiry and
device usage when they have not — never an opaque id.

What this needs on the website side is modest: list the customer's own active services with their
codes (the module can already read a code from a service), remember which one they picked, and keep
both behind the same signed-in session as everything else.

Sharing is unaffected. The family member signs in with *their own* address, sees nothing, and types
the key the buyer sent them — which is right: we must never hand someone else's key list to whoever
happens to sign in.

The **double-charge disappears for free.** Once an active website service makes the app premium, the
purchase screen is not offered, and the existing "you already have a subscription" path covers it —
no new refusal rule to write.

### Their subscription came from a different store than the app they are using

It still works. Premium follows the account across platforms, so a subscription bought on one store
opens premium in the app on another.

The only difference is cancelling: we can only offer a link to the store that actually billed them.
When the app cannot open that store, it says so in neutral words instead of pointing at the wrong
place.

### They want to delete, and their subscription came from a different store

Deletion works normally — there is one account regardless of who billed it. But the warning matters
more here: we cannot cancel a subscription in **any** store, and the store they must go to is the
one they bought from, which may not be the platform they are holding.

### They want to delete, and they bought on our website

> **Decided 2026-08-13 — not how it works today.** Today deletion is *refused* while any website
> service is active, which forces an unfair choice: cancel a key you already paid for, or keep an
> account you want gone. For a one-year key that means waiting a year, and a deletion you must wait
> a year for is not a deletion the stores accept.

**Deletion goes ahead. A website purchase never blocks it.** The old refusal existed to stop a card
charge being orphaned behind an erased person — and cancelling the billing does that directly, so
the refusal has nothing left to protect.

1. **Cancel the billing, do not terminate the service.** Every website-billed service is set to
   cancel at the **end of its paid period**, so no renewal invoice is ever generated and the key
   still runs out the time it was bought for. Immediate termination would destroy something they
   paid for, which is the one thing we never do.
2. **Show the keys one last time**, with a plain warning: *these stay active; save them now, because
   after this we cannot show them to you again.* Then delete.
3. **Drop the stored payment method.** Even with nothing scheduling a charge, a card token attached
   to an erased customer must not survive.
4. **Keep the agreement reference, not the person.** The deletion journal holds the gateway's
   subscription id beside its numeric ids — a contract reference, not personal data — because
   without it nobody can find the agreement to cancel once the customer is anonymised. Deletion must
   not destroy the only thing that can stop the billing.
5. **A payment that arrives anyway is refunded once, and the agreement is killed.** A charge landing
   for an erased account is a **defect signal**, not a routine event: refund the money, alert an
   administrator, and cancel the agreement at the gateway. A *second* charge on the same agreement
   must be impossible — if one arrives, the system failed, and that is a bug rather than a cost to
   absorb. Refunds are not free: most processors keep the original transaction fee and some add a
   refund fee, so a monthly charge-and-refund cycle would bleed money forever for a service nobody
   is receiving.
6. **The alert goes to an administrator, who cancels the billing by hand.** That is the intended
   mechanism, not a fallback — so the alert must carry the agreement reference from rule 4. An alarm
   nobody can act on is not an alarm.
7. **The customer is not told, and does not need to be.** Their address is gone — that is what
   deletion means — so the refund receipt reaches them from the gateway instead, which is right: it
   comes from whoever is actually holding the money.

Step 2 is what makes this fair. The policy is sound — a bearer key is theirs to keep safe, and
whether they saved it is not our business — but it has to be *said once, at the only moment it
matters*. Without that, every case becomes a support request we can never resolve, because the link
between the person and the key is exactly what deletion destroys.

**Why nothing needs to block.** Most gateways charge only when WHMCS asks them to, so cancelling the
billing ends it outright. A gateway that keeps its own schedule can still send one more charge — and
rule 5 catches it: refund, alert, cancel. Every gateway lets a merchant end an agreement from its own
dashboard, and many expose it through an API that WHMCS can call where the module supports it. So an
un-cancellable gateway is a tooling gap to close, never a reason to trap someone in an account.

**Their keys are untouched, and so is the device they deleted from.** The codes those services
delivered were sold to the buyer, not lent to the account, so they keep running to the end of what
was paid for — and because a website key is filed as the buyer's own code (§8), the device that
deleted the account carries on working. Only account-granted premium dies with the account. We take
back what the account lent, never what the person bought.
### They have a premium code they typed in themselves

**Untouched.** A code the person entered by hand was never the account's to take away — it may have
come from a gift, a promotion or a friend. Only a code we attached to the account is removed.

### They are connected when they delete

The tunnel is dropped as part of the deletion. Premium ended, so a premium session must not keep
running.

### They never signed in

Nothing happens, because there is nothing to delete. Free and trial use needs no account and leaves
no account behind.

### They ask for a refund instead

Separate path, and separate from deletion. For a store purchase, the store decides. For a website
purchase, we decide — and if we refund, we keep an anonymous one-way fingerprint of the refunded
account for up to 24 months, purely to judge future refund requests. It cannot be turned back into
an address and it survives deletion; this is disclosed at refund time.

## 9. Where a person can do it

| Route | Who it is for | Why it must exist |
|---|---|---|
| In the app, on the account page | Anyone signed in — the fastest route | **Apple requires it.** An app that lets people create an account must let them delete it *from inside the app*. Sending them to a website instead is refused. The only exemption is for tightly regulated fields — banking, healthcare, identity-verified services — which we are not. |
| The website deletion page → client area | Someone who no longer has the app, or never used it | **Google requires it.** Play's data-safety declaration asks for a deletion URL a person can reach without installing anything. |

So it is **both, and neither is optional** — they are two different stores' requirements, not two ways
of doing the same thing.

Two rules that follow from Apple's wording: deletion must not require a phone call, an email, or a
support ticket, and it must not push the person out to a browser. Ours is a page in the app that
talks to our servers directly, so it satisfies both. The review notes should still say exactly where
it lives, because a reviewer who cannot find it treats it as missing.

The web route requires signing in; there is no unauthenticated deletion form, ever. Someone who
bought through a store and never set a password uses password recovery on the address they signed
in with.

### An account that started on the website

Apple's test is not *where the account was created* — it is *does the app support creating one*. Ours
does, so the requirement applies, and what it asks is simply that a person signed into the app can
delete the account they are signed into.

That is already what happens, because there is only one account. The app and the website are two
doors into the same customer record: someone who bought on the website and later signs in with a
matching address is attached to that record, and deleting from the app erases it — customer,
identity, invoices and all. This is exactly why the "active website services" check had to exist:
deletion started in the app reaches all the way into the website side.

What is **not** expected is that we delete accounts the app cannot see. A person who bought with an
address that does not match their sign-in is invisible to the app — that is what the web page is for.

**This is why the refusal had to go.** It told the person to *cancel them in the web client area
first, then delete* — sending someone to a website to finish deleting is the precise pattern the
rule exists to prevent, and for a customer whose account began on the website that was the normal
path, not an edge case. §8 replaces it with cancelling the billing outright, so the in-app deletion
always completes.

## 10. What we promise before they confirm

The confirmation must carry the whole contract, in store-neutral words — it ships in one app on
every platform, and naming a competing store is itself a store violation. It must say:

- The account and personal data are permanently deleted and cannot be restored.
- Every signed-in device is signed out and loses premium immediately.
- This does **not** cancel the subscription — it belongs to the store where it was bought and may
  keep renewing until cancelled there.
- While that subscription is still running, it can be brought back onto a new account.
- Invoices are kept for legal reasons.

The privacy policy and the public deletion page must say the same thing. If any of the three
disagree, the one that promises the most is the one we are held to.

## 11. Open questions

Recorded rather than answered — do not assume a behaviour that is listed here.

1. **What triggers recovery?** §7 needs something to ask the store, at sign-in, what this person
   owns. Whether that is silent or a button the person presses is undecided; silent is better for
   the person, a visible control is safer with Apple, and doing both is possible.
2. **Can two accounts hold the same premium code?** If a returning person recovers a code that
   another account still holds, does the older link go away? Without a rule, delete-and-recreate is
   a way to farm duplicate active services.
3. **What happens to a device that never comes back online?** It keeps a working credential
   indefinitely. Acceptable today because the credential expires with the subscription, but it is
   the reason no service may ever be left without an expiry.
4. **Deleting while a subscription is in a grace or hold period** — is that a normal deletion, or
   should it be treated like an active subscription?
5. **Should the website cap how many subscriptions one person may hold?** Today it is unlimited,
   which is right for someone buying codes as gifts and wrong for someone who bought twice by
   accident and will ask for a refund. If we ever cap it, the cap belongs on the *website* only —
   the store side already refuses a second.
6. **A key that is valid but out of device slots.** It is not expired, so it counts as usable by the
   rule in §8, yet applying it produces a connection that fails. Showing the slot count in the
   picker and letting them choose it anyway is probably right — they may want to free a slot — but
   applying such a key *silently* would look like a broken app.
7. **A reseller with fifty keys** would meet a fifty-row picker. The rule is still correct (never
   guess), but the screen is not. Some cap, search, or "type the code instead" escape is needed
   before anyone can sell in bulk to a person who also uses the app.
8. **The email has to match, and often it will not.** Everything in §8 — the link to their website
   purchase, and the behaviour built on it — depends on the address they signed in with being the
   address they bought with. Buying with a work address and signing in with a personal one is
   ordinary behaviour, not an edge case, and today it silently produces two unrelated customers.
   Whatever we do about it (a second verified address on the account, or claiming a purchase by
   entering its code once), the manual paste has to stay as the way out.
9. **Should invoices be anonymised at all?** Needs an accountant's answer for the countries we
   invoice from, because it may be the *anonymising* that is unlawful, not the keeping.

   Erasure was never required: the right to erasure does not reach data we are legally obliged to
   retain, and tax retention is that obligation. So anonymising goes further than the law asks —
   and going further is where it can break:

   - **A business customer's invoice must name them.** Anonymise it and they lose a document they
     need for their own tax deduction, and we may be unable to reproduce a valid one.
   - **Some countries require billing records to be provably unalterable.** Editing a stored invoice,
     even to remove a name, is the thing those rules exist to prevent.
   - **Where invoices are already reported to a tax authority**, that authority keeps its own copy
     with the name. Anonymising ours changes nothing for the person and takes the risk anyway.

   It looked safe because our sales are small consumer amounts, where most countries allow a
   simplified invoice carrying no customer details. That holds only while every sale is small and
   consumer.

   The conservative alternative is **retain and restrict** rather than anonymise: keep the invoice
   intact, lock it out of ordinary use, and delete it when the retention period ends. The stores
   accept legally-required retention as long as it is disclosed, so the only cost is saying plainly
   that invoices keep the buyer's name for N years. It also preserves something anonymising destroys
   — the ability to prove who bought what when defending a chargeback.

### Answered elsewhere

These came up and are now settled — kept here only so they are not re-opened.

| Question | Answer |
|---|---|
| Does a website purchase block deletion? | No. Billing is cancelled instead — §8 |
| Should a one-time purchase block it? | No. Nothing blocks it — §8 |
| Can a merchant end a gateway agreement? | Always, from the gateway's own dashboard; an administrator does it when alerted — §8 |
| Who pays for a refunded stray charge? | We do, once — which is why it comes with a cancellation, so it can never repeat — §8 |
| A renewal arriving after deletion | Recorded, the entitlement stays alive for later recovery, and the person is never resurrected — §7 |
| Does the app help someone holding several codes? | Yes — §8 |
| Do we refuse a store purchase to an existing website customer? | It is never offered: they are already premium — §8 |

## 12. Where the wording lives

Three places must agree, and all three are translated:

- The confirmation the app shows before deleting.
- The **Delete Your Account (Forget Me)** section of the CONNECT privacy policy.
- The public account-deletion page on our website.

Change one, change all three. The English is authored once and every other language is generated
from it.
