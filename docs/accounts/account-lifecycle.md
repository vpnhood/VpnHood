# Account lifecycle — business flow

*Last reviewed: 2026-08-19*

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
9. [Where a person can do it, and what each store allows](#9-where-a-person-can-do-it-and-what-each-store-allows)
10. [What we promise before they confirm](#10-what-we-promise-before-they-confirm)
11. [Open questions](#11-open-questions)
12. [Where the wording lives](#12-where-the-wording-lives)

Section 8 answers these, in order:

- They already have a subscription and try to buy again
- They order a second subscription on our website
- They bought in bulk, to resell
- They bought on our website, then sign in to the app
- Their serving credential is refused
- They sign in with a different address than they bought with
- They cannot reach us at all, because we are blocked where they are
- Their subscription came from a different store than the app
- They want to delete, and their subscription came from a different store
- They want to delete, and they bought on our website
- They delete while the subscription is between payments
- They want us to stop the payments, not just the account
- They have a premium code they typed in themselves
- They are connected when they delete
- They never signed in
- They ask for a refund instead

## The short version

| Question | Answer | Where |
|---|---|---|
| Who has an account? | Only someone who signed in — to buy, restore, or carry one importable code across devices | §3 |
| Website buyer signs in — premium? | Yes — on the code marked as theirs, automatically | §8 |
| Which code, if they own several? | Ranked on every read: the device's own store's subscription first, then any portal code being paid for right now, then the other portal codes known to be valid, then the imported one. Deterministic, with no dates in it. Nothing is stored as the selection | §8 |
| Can they change it in the app? | No list or picker. Where code entry is supported they type one into the profile and the app uploads it; signed-in Remove does not exist, and inventory lives in the client area | §7, §8, §9 |
| Two subscriptions? | Any number of codes from the portal store (for sharing); one subscription per store — and one at each store can coexist: prevented up front, accepted and surfaced if it happens | §8 |
| Do we ever refuse a purchase? | Never after the money moved. Prevention happens before the store's payment sheet; whatever arrives paid is provisioned | §8 |
| Is a code safe to hand out? | Yes — it carries its own device limit, whoever holds it | §2 |
| What does deletion erase? | The person, on every device. Premium granted by the account dies with it | §5 |
| What does deletion keep? | Their store subscription, the codes they bought on the website, and invoices frozen with the buyer's name | §6 |
| What blocks deletion? | **Nothing.** Billing is cancelled at the end of its paid period instead | §8 |
| Coming back? | A new, empty account — the store is asked at sign-in and gives the subscription back by itself; the code we emailed on the way out still works | §7 |
| Blocked where they are, so they cannot sign in? | Connect first on the free or trial path, then sign in through the tunnel. No portal clock removes the applied code; only an access-server refusal starts the ending flow | §8 |
| Does a refund end a code? | Only if we end it. Revoking is the default; keeping it is a choice | §8 |

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
| **The subscription** | Proof that money was paid, and the promise to keep charging | The store that sold it — our website is one of the stores (below) | **Yes** at an outside store — that store's account is not ours to touch, and signing in gets it back. At the portal store, the account being deleted *is* the store account, so its billing is cancelled at period end |
| **The access code** | What we actually sell, and what opens premium on a device: a short, typable string carrying its own expiry and device limit | Us | **Yes**, on our servers — but it belongs to nobody, and stops working on every device |

### Our website is a store — the portal store

Every subscription is born at a store, is managed and cancelled at that store, and lives exactly as
long as the buyer's account **at that store**. Our website is one of those stores — the **portal
store** — and every build of the app has a **home store**: the store that distributes it, or the
portal store for the builds we distribute ourselves.

The portal store differs from the app stores in two capabilities, both deliberate (§8): it sells
**bearer codes in any quantity** — gifts, family, bulk — where an app store sells only a
subscription on one store account; and it is the one store **we operate**, so it is the only place
where a refund is our decision and where deletion can stop the billing itself.

And one identity carries most of §5 and §6: **the portal store's account is the VpnHood account
itself.** An Apple or Google identity merely attaches to our account; the portal credentials *are*
it. That is why deleting the account cancels portal-store billing and touches nothing at any other
store — it is one event seen from two sides: deleting your account at a store ends that store's
subscriptions. Theirs when they delete their store account; ours when they delete ours.

### What we call it: the access code

**We sell access codes.** That is the unit a customer buys, holds, saves, types and gives away, and
it is the word used everywhere below — shortened to **code** in ordinary sentences.

Nothing here is called a "key" on its own. That word is already taken in this product by the
**access key** — the long string carrying server addresses that lets a client connect at all, which
is never sold and never personal, and which this document never means.

The app says **premium code** where it speaks to a customer, and that is the same thing under a
friendlier name — used below wherever the sentence is one a person actually reads.

Whatever a code resolves to inside our servers is implementation. No customer and no merchant ever
sees it, nothing is priced or sold in those terms, and not one answer below would change if it were
built differently tomorrow. **The code is the product.**

The access code is deliberately **not** personal data: it is a random string that opens a gate. It
carries no name, no email, nothing about who holds it. That is why we can keep it after erasing the
person — and why keeping it is not a privacy compromise.

### Why a code can be handed around safely

A code **carries its own device limit**, and that limit is enforced wherever the code is used, no
matter which account — or how many accounts — hold it. Ten people with the same code still get the
number of devices that code was sold for.

That is worth being exact about, because the intuitive reading is the dangerous one: a code is not a
container of entitlement that copying multiplies. Every copy lands on the same allowance, so
**copying it creates nothing.**

This is the quiet foundation under most of the answers below. It is why we can mail a code to
someone whose account is being erased, why a code can be pasted into a fresh install, and why nobody
gains anything by holding the same code twice. **Sharing a code was never the risk.**

**Who enforces it, and why nothing in this document decides it.** The limit is applied by the access
manager at connection time, under its own policy — how many may connect at once, and how a device is
counted. A device is recognised by a random identifier the app generates per installation, sent in a
form only a server that can resolve the code is able to read: no account, no name, nothing that
identifies a person. The same device reconnecting does not consume a second place.

That policy is deliberately **not** settled here. It belongs to the access manager, it can change
without changing anything about accounts or purchases, and the app cannot see the count in any case.
What this document depends on is only the guarantee above — that a token cannot be stretched by
spreading its code around — not the particular number or the way it is counted.

The risk it does *not* cover is **minting** — one purchase producing two *different* codes, which
would double the device limit. Hence the rule that matters:

> **One purchase, one code, for the life of that purchase.** Proving the same purchase again always
> returns the same code, never a new one. Renewals extend it; they never replace it.

Two other things the device limit does not do, so they are handled separately:

- **It does not limit time.** Every code must carry an expiry, and it stops working when the paid
  period ends. Nothing may ever be issued without one.
- **It does not react to a refund.** A refund does not expire a code by itself — see §8.

The subscription is the one thing we do not control. Whoever took the money owns the renewal.

## 3. Who has an account at all

Most people never do. The app works without one.

An account is created the first moment someone signs in. The reasons are to buy a subscription,
bring one back, receive a website purchase, or carry one importable code across devices — including
into a codeless build. So:

- Never signed in → **there is nothing to delete**, and the app should not offer it.
- Signed in → exactly **one** account, whichever platform they signed in from.

There is one account per person, not one per device and not one per store. Signing in on a second
device joins the same account.

## 4. Life of a subscription

1. The person signs in. We create their account, or recognise the existing one.
2. They buy through the store on their device. The store takes the money.
3. We check the purchase with that store, and record that this account now holds a subscription.
4. We attach a premium code to the account and the app starts using it.
5. Premium follows the person, not the device: every device signed in to the account is served —
   by its own store's subscription where the account holds one, otherwise by the account's code
   (§8).

Renewals are the store's business. It tells us the subscription renewed; nothing on the device has
to happen.

### When a code starts counting down

Two different answers, and the difference is what was sold:

- **A prepaid one-time code starts on first use.** No expiry is set when it is bought, so one bought
  in January and given away in June runs its full term from June. This is what makes a code a
  sensible gift.
- **Anything billed on a cycle expires with the cycle.** A subscription is paid for a calendar
  window — the store charged for March — so the code runs to the end of that window and is pushed
  forward each time it renews. It cannot start on first use: a cancelled subscription would then
  keep working past what was paid for, and anyone could park a subscription unused and stretch it
  indefinitely.

This holds the same way whether the subscription came from a store or from our website.

## 5. Deleting an account

The person taps **Delete my account** in the app, or does the same from the website client area.
Both do the same thing — there is only one account.

Before anything is erased, we tell them what it means (§10). Then, in this order:

1. **Stop the money first.** Every service bought on our website is cancelled at the end of its paid
   period, so no renewal invoice is ever generated and the code keeps working until the time they
   bought runs out. Unpaid invoices are cancelled; paid ones are kept, and the stored payment method
   is dropped. Nothing here refuses the deletion — see §8.
2. **Warn them, without listing anything.** One serious sentence: *any premium code linked to this
   account will be gone, and we will not be able to find it for you again.* No list of codes, no
   count, no expiry dates — see below for why the screen deliberately shows nothing.
3. **Send one final message to their address, before it is erased.** This is where the codes
   actually go: every one they paid for — from website purchases *and* the code behind a store
   subscription — with the same warning, plus a note about any subscription still running. The
   confirmation screen is seen once and dismissed; an inbox is searchable a year later, which is
   when they will actually want the code. This is the last legitimate use of that address —
   confirming an action they just asked for — and it is not a new exposure, because a code bought on
   our website was delivered by mail in the first place.
4. **Erase the person.** Sign-in sessions on every device, the sign-in identity, the email address,
   the account itself.
5. **Cut the account free from its premium code.** The code is kept on our side, but it now belongs
   to nobody.
6. **Freeze the invoices, then erase the customer.** Each invoice is archived exactly as it was
   issued — buyer's name included — and the customer record and client-area login are then
   overwritten. The person disappears from the live system; the financial documents keep the
   identity the law requires them to carry. See below for why this replaced anonymising them.
7. **Write the journal entry** — numeric ids and the gateway's agreement reference, no personal data
   — so the anonymisation can be re-applied after a backup restore, and so a stray charge can still
   be traced to an agreement someone can cancel.

**Why the mail may carry the codes.** A code carries its own device limit (§2), so handing it back
gives away nothing beyond what was already bought, and it still expires when the paid period ends.
It is also the only thing that survives the erasure *usefully*: everything else we could keep to
help them later would be a record of a person we just promised to forget.

**Why the screen shows none of them.** Three reasons, all pointing the same way. A list on a screen
is read once under pressure and then lost, while an inbox is searchable years later — so the screen
is not the copy that would save anyone. Listing codes means the app must ask what the account holds,
which drags the whole inventory question into a client that has no other use for it (§8). And on a
platform where an app may not unlock anything with a code (§9), a screen full of codes is the one
surface a reviewer will read as exactly that. **The screen warns; the mail delivers.** The warning
must therefore be strong enough to stand alone, and it must not promise a recovery we cannot make.

### Why invoices keep the buyer's name

Invoices once came out anonymous. That choice was reversed: they keep the name they were issued
with, and only the *person* is erased.

Erasing someone was never supposed to reach financial records: the right to be forgotten does not
override data we are legally obliged to retain, and tax retention is exactly that obligation. So
anonymising was never required — and doing more than the law asks is where it started to cause
problems of its own:

- **A business customer's invoice has to name them**, or they lose a document they need for their
  own tax deduction and we may be unable to reproduce a valid one.
- **Some countries require billing records to be provably unalterable.** Editing a stored invoice —
  even only to remove a name — is the precise thing those rules exist to prevent.
- **Where invoices are already filed with a tax authority**, that authority holds its own copy with
  the name on it. Anonymising ours changes nothing for the person and takes the risk anyway.

It looked safe because our sales are small consumer amounts, where most countries accept a
simplified invoice with no customer details. That reasoning holds only while *every* sale is small
and consumer, which is not a bet worth carrying.

**Keeping a name after someone asked to be forgotten is lawful only if it is done properly.** Three
things have to be true together, and the third is the one usually forgotten:

1. **A legal basis** — the tax retention obligation. Already the case.
2. **Restriction.** The frozen invoice is out of ordinary reach: not searchable, not used for
   support, never used for marketing. It exists to satisfy an auditor, nothing else.
3. **Disclosure.** We say plainly that invoices keep the buyer's name. Retention that is disclosed is
   accepted, by regulators and by the stores alike; retention that is quiet is what turns into a
   complaint.

It also preserves something anonymising destroyed: the ability to prove who bought what when
defending a chargeback.

#### What we say, and why it names no number

The wording is: *invoices are kept because tax law requires it, for as long as that law requires,
and are used for nothing else.*

Data-protection law asks for the retention period **or the criteria used to determine it** — the
criteria are a permitted alternative, not a weaker substitute. Pointing at a specific legal
obligation *is* a criterion. What is not acceptable is the vague version, "as long as necessary for
our legitimate purposes", which names no criterion at all.

Naming a number would be the riskier choice, because **a number in a published policy is a
promise**:

- Too long, and we are keeping personal data past what we can justify.
- Too short, and we breach our own policy every time we keep a record the law obliges us to keep.

The retention period is set by where we are established, and across Europe it ranges from four years
to ten. A company that names a figure can do so because it knows its single jurisdiction. The
better-resourced privacy companies in our own market name no figure for billing data at all, and
that is the pattern to follow.

Internally we act on **about ten years** so nothing is deleted early — an operating assumption, not
a published promise. We never actually need the exact figure, because **nothing automates the
deletion of an invoice**: the day the number would matter is the day we start deleting, and that is
not something we are building. Automatic destruction of financial records is the very thing the
inalterability rules exist to prevent.

Billing is stopped first and identity erased last, on purpose: a half-finished deletion must never
leave a live charge behind an account that no longer exists. If any step cannot be completed, the
whole thing aborts with a message rather than half-deleting.

### The one thing a deletion may not do: break a legal hold

Our servers keep connection records — the time and the client endpoint — for 30 days, counted from
when each entry is written. They are not part of the account, they are not in the database, and
deletion does not reach into them: they simply run out their own 30 days and expire, which is what
the privacy policy discloses.

**A preservation request suspends that expiry.** If an authority formally asks us to preserve
records, or a claim arrives that we have to defend, everything it covers stops expiring until the
matter is resolved. The person is still erased on schedule — the hold protects log entries, never
identity — but those entries are not destroyed while it is open.

This is the single rule in this document that outranks a retention promise, and it is written down
because the instinct runs the other way. **Destroying records after being put on notice is worse
than keeping them**: routine expiry on a published schedule is a policy, the same deletion once a
request has landed is spoliation, and a court treats the two very differently. A deletion feature
that quietly kept working through a hold would turn our best privacy behaviour into our worst legal
exposure.

Two consequences worth stating plainly:

- **Expiry has to be suspendable per record**, not only globally. A hold that can only be honoured
  by stopping every rotation everywhere is one nobody will use.
- **The hold outlives the account.** It is the one thing that may still exist after step 4 has
  erased the person, and the only reason it is not a contradiction is that a held log entry no
  longer resolves to anyone — the identity that would have made it personal is already gone.

### On the devices

The account is gone, so premium goes with it — **on every device, not just the one that pressed the
button**:

- The device that deleted signs out and drops premium immediately, disconnecting if connected.
- Every other device discovers it on its next contact with our servers — at the latest, its next
  launch — and signs itself out and drops premium then.

There is no push, and no way for us to reach a device that is offline. A device that never runs again
simply never finds out; it holds a credential our servers no longer associate with anyone.

**That is not a leak, for two reasons.** A code does nothing while it sits on a device — it only has
an effect at the moment of connecting, and connecting *is* the check. So there is no window in which
holding a stale code is worth anything: the moment it is used, the server decides. And a device that
does keep working is in exactly the position of someone who kept the code we mailed them (step 3
above) and used it again. What would once have looked like a gap is the behaviour we chose.

**So one rule carries all of this, and it is the one in §2: nothing may ever be issued without an
expiry.** Every other protection here is a convenience. That one is not, and it is why it is written
as an absolute.

One cosmetic consequence, which resolves itself: a device that has been offline may still show
premium in its own screens after the code behind it has expired, until it next reaches us or tries to
connect.

## 6. What deletion does **not** do

- **It does not touch a subscription at any outside store.** That store's account still exists, so
  the store keeps charging until the person cancels it there, and we deliberately do not try to
  stop it for them — see §8. We say this before they confirm. Portal-store billing is the same rule
  seen from the other side: the account being deleted *is* that store's account (§2), so its
  billing is always cancelled — not a choice either.
- **It does not refund anything.** Refunds are the store's decision for store purchases, and ours
  only for website purchases.
- **It does not erase invoices, and does not strip the name off them.** We are legally required to
  keep financial records, and a financial record has to say who bought. They are frozen as issued and
  locked out of ordinary use — see §5. This is disclosed, with the retention period, before anyone
  confirms a deletion.
- **It does not take back the codes they paid for.** They were mailed them on the way out (§5), and
  those codes run to the end of the period they were bought for. (They do leave the person's own
  signed-in devices with the account — re-entering the mailed code is what brings one back; §8.)
- **It does not erase everything the same instant.** Connection records already written to our
  server log files run out their own 30 days, and database backups roll over within the same
  period. After that they expire.
- **It does not break a legal hold.** Records covered by a preservation request or a live claim
  stop expiring until the matter is resolved — the one thing that outlives an erasure, and the one
  retention rule that outranks every other in this document. See §5.
- **It does not erase what other companies hold in their own right** — the sign-in provider, the
  store that billed, the payment processor. They keep their own records under their own policies.

## 7. Coming back afterwards

Signing in again creates a **brand-new, empty account**. The old one is not restored, and we keep no
way to recognise the returning person — that is deliberate.

What they paid for is still theirs, and there are **three ways back to it**. None of them requires us
to remember who they were, and the first two need nothing from the person but signing in.

### Asking the store — the main route

Anyone who bought in a store gets it back by signing in, and does nothing else. The app proves the
purchase to the store, and we match that proof back to the code. **The same code comes back, not a
new one** (§2) — otherwise every delete-and-return would mint a fresh service.

It happens **as part of signing in**, quietly, with nothing to tap: right after the session is
established the app asks the store what this store account owns and presents anything we do not
already know. A **visible restore control stays** alongside it — Apple expects to find one, and it
is the way out when the store account signed in on the device is not the one that bought.

Four conditions make it safe to put in the sign-in path:

1. It must be the **silent** kind of store query, the one that reads what the device already
   knows. The older kind asks for the store password, and that must never happen on every sign-in.
2. Presenting a purchase we already know is a **no-op**, never a second purchase.
3. A failed store query **never fails the sign-in**. Offline just means not premium yet, and the
   visible control is the retry.
4. Sign-in **never waits** for it. It completes, and premium appears when the answer arrives.

**No ownership fight is possible**, and this is worth stating plainly for anyone reviewing the
design: because the device limit rides on the code (§2), a code being reachable from more than one
account grants nobody an extra device. There is nothing to take away from a previous holder, and no
rule is needed about who wins.

### Signing in to the website account — the route for a website buyer

Asking the store only works for someone who bought in a store. A website buyer has no store purchase
to prove, so they sign in **as their website account**, with the same credentials they bought with,
and the code attached to that account is applied for them (§8).

This is offered on **every platform**, not only where a code can be typed, and it is what makes the
website channel work on a platform that forbids codes entirely (§9). It also removes a whole class
of problem rather than working around it: the mismatch between "the address I bought with" and "the
identity I signed in with" cannot occur when they are the same account by construction. Nothing to
claim, nothing to attach, no support ticket.

Two limits, both deliberate:

- **Sign-in only.** No account creation and no pricing inside the app. Account-based access to
  something already bought is expected; a signup or checkout path pointing away from the store is
  not, outside the storefronts that now permit it (§9).
- **It does not help someone who was given a code.** A gift recipient has no website account. Their
  route is the third one below, or — where that does not exist — importing the code into an account
  of their own from a typed-code build or the client area first (§8). Importing consumes nothing:
  the friend who gave them the code is unaffected, and so is anyone else already using it.

### The code they kept — the route that needs no account at all

If they kept the code we mailed at deletion (§5, step 3), or were handed one by a friend, they
**paste it in**. Nothing else is needed: no account, no store, no network round-trip to prove
anything. The app files it as their own code, exactly as it treats a code from a gift or a
promotion, and the device limit that came with it still applies. Pasting is local first: the code
works on that device whether or not an account exists.

On a signed-in device, the app then offers a **separate deliberate act**: save this code as the
account's one imported code, so the person's other devices — including a build that cannot take a
typed code — can receive it (§8). It is never automatic. One informed confirmation says that saving
will replace any code already saved to the account; one idempotent PUT then sets the slot atomically.
The app and client area can also DELETE that slot. Removal is one account-wide act, applied by other
signed-in devices on their next successful refresh; it is described as **Remove access code**, not
as switching editions. The portal can only attach an individual code it can match to a service; a promotion,
access-manager-issued code or reseller CSV code may therefore keep working locally while the save
answers honestly that it cannot be attached — said once, plainly, after which the app stops
offering; since the string in the person's hand may be the only copy there is, it also says to keep
it. The local success never depends on the account save.

This works on a phone that has never seen their account, on a platform they did not buy from, and
years later. It is the reason step 3 of §5 exists — a person who keeps their code can never be
locked out by anything we do afterwards.

**It is not available in every build.** Typing a code is a capability a build either has or does
not, and on at least one platform it may not (§9). Where it is missing, the two routes above carry
everything, and a bearer code reaches its holder through the website instead.

## 8. Situations and answers

### They already have a subscription and try to buy again — including from a different store

**We prevent it before the money moves — and we never refuse it after.** A purchase that reaches us
paid for is accepted, whatever else the account holds.

This is a reversal of an earlier design, in which the server refused the purchase after the fact
and relied on the store to refund it. That refusal was safe on exactly one store: Google Play
automatically refunds a purchase that is never acknowledged as delivered. Apple has no such
mechanism — no acknowledgement deadline, no automatic refund, and no way for us to cancel or refund
a subscription from our side. There, a refusal is us keeping someone's money and delivering
nothing. A rule that can only be enforced by holding money that is not ours is not a rule we can
have — so prevention carries the whole weight, and acceptance covers what prevention misses.

#### Prevention, in both places it can exist

| Layer | What it does |
|---|---|
| **The app** | Checkout never opens for an account that is currently served. Signing in comes first, the account is resolved, and someone whose credential still succeeds sees premium instead of a price. After `AccessExpired` or `AccessCodeRejected`, **Restore Premium** may offer a purchase even though the refused code remains stored. Prevention must finish **before the store's payment sheet appears**, because after it there is no undo |
| **The portal store** | The checkout warning (below, *a second subscription*): someone already holding something active is told what they hold before they pay. It warns and never blocks — the portal sells codes in any number on purpose |

The servers are authoritative in both: the portal says what the whole account holds, and the access
server says whether the serving credential is actually accepted. The app never infers the answer
from a displayed expiry or its own clock.

#### Acceptance, when a purchase arrives anyway

An old build, an interrupted flow, a race — or simply a person who subscribed at one store and
then, on another platform, subscribed again. Whatever arrives paid is provisioned like any other
purchase: it belongs to the account, it is visible in the client area beside everything else, and
it serves its own store's builds (*which code the account gets*, below).

**It is never silent.** A purchase landing on an account that already holds an active one is
surfaced, not absorbed: the client area shows both, and one message says so plainly — what serves
this device, and that each subscription is cancelled at the store that sold it. Both purchases are
real — each store sold a subscription that genuinely serves that store's devices — so neither is a
mistake to unwind by force. Whether to keep both is the person's decision; our job is to make it a
decision rather than a surprise.

#### Switching stores

There is no way to move a subscription between stores — each belongs to the buyer's account *at
that store* (§2). So switching is done the only honest way: **cancel at the store that billed, let
the paid time run out, then subscribe at the new store.** The moment nothing serves the account any
more, checkout opens again by itself. There is no button in the app for any of this, deliberately:
the cancel lives in the old store, where all of that subscription's management does.

A portal-store code is never a wall in the meantime: the portal sells any number of codes (below),
so someone who wants another code buys one at any time. What waits for expiry is only a *store*
subscription while something still serves the account — the store model working, not a gap in it.

### They order a second subscription on our website

**Nothing stops them, and that is on purpose.** The website sells a *code*, not a seat on an
account: a person can buy two, five or fifty, and each one is an independent service with its own
code, its own billing cycle and its own cancel button. Selling many at once is an explicit product
mode for resellers.

The business reason is **sharing**: someone buys several codes and gives them to their family. The
stores cannot do this — a store subscription belongs to one store account — so the website is the
only place it can happen, and the difference between the channels is deliberate, not an oversight.

This is the portal store's deliberate difference from the app stores (§2), and the difference is
what is being sold:

| | Bought in an app store | Bought at the portal store |
|---|---|---|
| What the person gets | Premium on **their account** | A **code**, which they hold and can give away |
| A second purchase | Prevented up front; accepted and surfaced if it lands anyway (above) | Allowed, any number |
| Who can use it | Whoever signs in to that account | Whoever has the code |
| Cancelling | Only in that store | In the website client area |

So a store subscription is tied to a person, and a website code is a bearer good — closer to a gift
card than to a seat. Someone buying a second website subscription is usually doing it for a family
member, and refusing that would break a normal sale.

#### Should there be a limit?

**No limit. A warning at checkout instead.**

A limit is the wrong tool. Nobody buys a fifth code by mistake; they buy a *second* one, because they
forgot they had one or thought it had run out. A limit set at three does not prevent that, and a
limit set at one destroys the product. It would also break the two cases the website exists to
serve — buying for the family, and selling in bulk.

The accident is worth preventing, but it is a **confirmation** problem, not a quantity problem. So
when someone who already holds something active reaches checkout, they are told what they have
before they pay, and the wording depends on **whether what they hold will continue on its own**:

| What they already hold | Buying again is | What they are told |
|---|---|---|
| A subscription that renews itself | Usually a mistake | It renews on its own, and a second purchase is a separate code, not an extension |
| A code they have never used | Almost certainly a mistake | They have a code they have not used yet |
| A code in use with time left | Possibly deliberate — a gift, or family | What they hold, and when it runs out |
| A code expiring or already expired | **The correct action** | Nothing at all |

Three rules govern it:

1. **It never blocks.** One step past it, always. A warning that cannot be passed is a limit wearing
   a different hat.
2. **It stays silent when buying is right.** The last row matters as much as the first three: a
   warning that fires when the purchase is correct teaches people to dismiss it unread, and then it
   no longer works for the case it was built for.
3. **Where several are held, it describes the one that bears on the decision** — the longest-lived,
   or the one that renews itself. Never an arbitrary one. Telling someone about an unused code while
   their live service is days from lapsing is worse than saying nothing, because it suggests they
   are covered when they are about to lose it.

Bulk and reseller orders skip it entirely: there is no interactive checkout, and a warning repeated
fifty times is noise.

**One reason a limit gets proposed that it cannot solve.** An unlimited quantity field is a fraud
amplifier — fifty codes bought on a stolen card and resold before the chargeback lands. That risk is
real, but a per-person limit does not touch it, because fraud uses fifty accounts rather than one.
Order velocity limits and manual review above a value threshold are the tools for it, and they
belong with the payment controls, not here.

The consequence for deletion is that the billing cancellation in §5 covers **all** of them.

### They bought in bulk, to resell

Bulk selling is a separate product, offered only to merchants we choose, and it behaves differently
from every other sale in this document.

A bulk order is **stock, not service**. Someone buying fifty codes is buying inventory to sell, not
fifty things to use. Three consequences follow, and all of them are deliberate:

1. **The codes are delivered as a file**, once, at purchase. There is no single code to look up
   afterwards, so the client area shows the delivery rather than a code.
2. **Stock is never treated as the buyer's own code.** It is never offered in the app as "your code"
   and never becomes their default. A reseller who wants to use one of their own codes enters it by
   hand, like anyone else holding one.
3. **Suspending or cancelling a bulk order does not switch the codes off by itself.** They were
   handed over as a file and they keep working. **An administrator disables them directly, and the
   system says so loudly** — it refuses the operation with a message naming the batch, rather than
   reporting success for something that did not happen. Doing it by hand is the decision, not a
   limitation: automating it would mean tracking every code in every batch, and the volume does not
   justify that while bulk delivery goes only to merchants we have chosen.

Point 3 is the one that matters commercially, so it is worth stating plainly: **a reseller who takes
delivery and then does not pay keeps working codes until someone acts by hand.** That is a deliberate
trade — bulk delivery is offered only to merchants we have chosen, and choosing them is the control.
It is not something the billing system enforces on our behalf.

### They bought on our website, then sign in to the app

**Signing in makes them premium, on the code that is theirs.** The two channels meet at the account:

1. Signing in **attaches them to their existing website customer record if the email matches** — the
   one the sign-in provider proved. Signing in never creates a customer record on its own; that
   happens only at a first store purchase.
2. The app then asks what the account holds — the store purchases **and** the website codes the
   account can see — and applies one by the rules below.
3. If they signed in with a different address than they bought with, they are two unrelated
   customers to us until they import their code — see *They sign in with a different
   address* below.

Most website buyers buy **one** code, for themselves. Making that person type a code they have
already paid for would be a step with no purpose and a support ticket waiting to happen. So signing
in makes them premium — and the ambiguity only appears when they hold more than one.

The rule that resolves it is *the account exposes exactly one selected code, and inventory stays
server-side*. The selection is changed by deliberate acts — saving the one imported code or
choosing a purchased code in the client area — never by handing an inventory to the app:

| Situation, in order | What the app does |
|---|---|
| A **serving** subscription at this build's home store | Use it — the device's own store comes first (below) |
| The signed-out device runs on a code the person **typed** that has not been refused | Leave it local |
| The account has a code | Use it — exactly one is handed over: whatever the ranking picks (below) |
| None | Signed in, not premium — as today |

Read top to bottom; the first row that matches wins. On a signed-in typed-code build, entering a code
uploads it to the account's slot without asking (§8, *the one upload slot*) — typing a code is
choosing to use it. It never asks the person to choose from an inventory, because the app is never
handed more than one code to consider — see below.

#### The app is told a code, not a list of codes

**The app is handed one code or nothing.** No list crosses to the device, there is no picker, and
there is no "several" case for it to reason about. The inventory question belongs to the side that
holds the inventory.

This is written as a rule rather than left to judgement, because the alternative looks more helpful
than it is: hand the app everything the account holds, let it work out whether one is chosen or
whether there are several, and show a picker when there are. Owning several codes is **rare** — it
means buying for family or for friends — and a rare case does not justify permanent complexity in
every copy of the app. A client that knows nothing about an account's inventory is a client that
cannot get it wrong, on any platform, in any version still installed.

**Where a person genuinely wants a code other than the account's standing selection, they have two
routes, and which one they get depends on the build:**

| Which build | How they change it | Why |
|---|---|---|
| One that takes a typed code | Signed out, they type a local code. Signed in, one confirmation sets or replaces the account's saved code | Account-managed devices share one explicit selection; a local-only code remains possible without an account |
| One that does not (§9) | They name the code in the **client area**, on the website | There is no code box to type into, so the choice has to live somewhere the store does not govern |

That second row is a requirement, not a fallback. A platform that forbids unlocking with a code
takes away the only in-app answer to *"that is the wrong code"*, so the client area has to carry it
— without it, those people have no way to choose at all. It is the one piece of code management we
keep, and it belongs in the place where someone who owns several codes is already looking: next to
their invoices and their services.

#### Which code the account gets, and whose choice it is

**A device asks its own store first.** Every build has a home store (§2): the store that
distributes it, or the portal store for the builds we distribute ourselves. A subscription the
account holds at that store, while it is **serving**, beats everything else for that device — it is
what the person bought for exactly this platform, and it is the one subscription whose manage and
cancel pages this device can actually open. *Serving* is the store's own state: a subscription in
**grace** (the store is retrying a failed payment and access continues) is serving; one in **hold**
is not, and the device falls back to the rules below until the payment recovers.

One consequence is deliberate: two devices of one account, on two platforms, may be served by two
different purchases. That is the nature of per-store subscriptions, not a defect — and the client
area is where the whole account is visible in one place.

**An account holds codes, and all of them are treated the same way.** Nothing is stored as *the*
selection: the portal ranks what the account holds and recomputes the winner on every read, so a
code that dies leaves nothing to repair (keyring plan §2):

1. **Whatever is being paid for right now comes first** — the store subscription for that device's
   own store while the store is still charging for it, otherwise a portal code with live recurring
   billing. Someone who is paying never does code management, and an ordinary additional purchase
   cannot displace it. A subscription that has **ended** — refunded, expired, or simply run out —
   stops being one of their codes the moment it ends; cancelling is not ending, because the period
   already paid for is theirs.
2. **Then the other portal codes**, and last **the imported code** while it is eligible. Within a
   group, a started clock before one that has not begun — an unused one-time code is worth more
   unspent — and then oldest purchase first.
3. **Then nothing.**

   No dates steer any of this. An expiry the portal can read is *display*: a clock that could retire
   a code could equally start an unused one early, and only the access server knows. A code leaves
   the ranking when the person parks it, or when a device reports a refusal — and never when it is
   the thing being paid for right now, because downgrading a payer to a lesser code would hide our
   own provisioning fault. That last rule is also what makes renewal recover by itself.
4. **The account has one upload slot, and it is a stored string.** Uploading takes any well-formed
   code on trust — validity is settled at use time by the access server, never at save time by the
   portal — so there is no *not found* answer and nothing to inspect in the reply. Uploading a
   different code replaces what is there; uploading a code the account already owns does not consume
   the slot and turns that code back on for the ranking instead. There is no prompt: typing a code
   *is* choosing to use it. The one question that remains is asked BEFORE signing in, where the
   choice is still free — sync it, sign in without it, or cancel.

**Two reversible marks steer the ranking, and neither deletes anything:** `isAutoSelectable`, set in
the client area and **true by default**, is how somebody protects a code they bought to give away;
and **rejected**, set by the system when a device meets an access-server refusal. They stay apart on
purpose — a refusal must not erase a deliberate *keep this for later*, and a retry must not re-arm a
code somebody parked. Adding a code again clears its rejection, which is the whole of Retry. The
system never removes a code — the only thing that leaves is the upload slot's previous occupant.

**Eligibility is one boolean, and a device is the only thing that sets it.** A device reports that
the access server refused the code it was serving (`POST /v1/account/access-code/rejected`) — no
expiry, no reason, no timestamp, and nothing at all when a connection succeeds. It is applied only
while that is still the account's current code, and it covers every entry holding that string,
because identical access codes are the same credential. Recorded per account, because a code is a
bearer string many accounts may hold and one account's report must never disable it for somebody
else using it perfectly well. One case is accepted rather than solved: remove a code and add the
same string back, and a delayed refusal from the old attempt can land on the restored one — the
recovery is one more Retry, and the alternative was an identity system for codes.

This replaces two earlier designs in turn (decided 2026-08-20). Expiry-driven **promotion** shipped
first and was retired because a prepaid code starts its clock on first use (§4), so a silent
promotion could spend a gift nobody decided to spend. Its replacement — a **stored last deliberate
choice** — was retired in turn: it meant a dead selection had to be repaired by hand, and every
device that held it needed telling. The ranking has neither problem, because there is nothing stored
to go stale. What the app must still break loudly is unchanged: when nothing is left to pick, it
says so and offers Restore Premium (*their serving credential is refused*, below) rather than
quietly becoming its own free edition.

**Nothing is ever asked at purchase time.** Checkout is the worst place to add a question, the buyer
often does not know yet who a code is for, and the answer can change the next day.
Entering website checkout through **Restore Premium** needs no extra question: the path already says
this purchase is for repairing the account, and the ranking picks its new code up by itself — a
purchase being paid for right now outranks everything else the account holds.

Three guardrails make the automatic part safe:

1. **Local and account-managed codes stay distinct.** A signed-out typed code is local and needs no
   portal. Once a signed-in person explicitly saves a code, it belongs to the account slot and
   follows that account's set/remove actions on every signed-in device. A code the portal cannot
   match stays local. A serving subscription still outranks either because otherwise the thing the
   person is paying for would buy nothing.
2. **The account selection belongs to the account, not to one device.** Every account-applied device
   lands on the same selected code, and that is the intent rather than a compromise: two phones
   belonging to one person should share one code. Separate codes exist for separate *people* — the
   friends and family who do not use the account. A serving home-store subscription and an explicit
   local typed code sit above this selection by the precedence table.
3. **Record it as a code the account granted, not as the person's own.** The account applied it, so
   it leaves with the account: signing out or deleting takes it off the device, exactly like the
   code behind a store subscription. Nothing is confiscated — the code itself keeps working for
   everyone using it, the farewell mail carries it (§5), and typing it back in — then optionally
   saving it from the app or client area — makes it the person's own in the ordinary way. An earlier
   design recorded it as the person's own so it would survive deletion; that put a bought-outright
   code under the app's remove control and let account premium ride into whatever account signed in
   next — both problems end with the flag.

   Sign-out removes account-applied state from this device but does not mutate the account slot or
   other devices. Its normal confirmation names sign-out; it does not invent a switch-to-free
   decision for builds that may not have a free edition.

**The upload slot is emptied in the client area.** It uses an idempotent
`PUT /v1/account/access-code` with `accessCode: null`; the app has no door to it at all (§7 — signed
out, Remove clears this device's own copy; signed in there is no Remove). The confirmation says that the account and its signed-in devices
will stop using the saved code, while the bearer code itself keeps working for anyone who still has
it. It never says **Switch to the free version**. What remains after deletion — a subscription,
another selected purchased code, a free capability, or no access — determines the resulting UI.

Removal is one account decision. Other signed-in devices apply it on their next successful account
refresh and do not ask again. It cannot be instantaneous on an offline device without revoking the
bearer code for everyone who shares it; this design removes only the account pointer. An unreachable
portal changes nothing until a refresh succeeds. No removal suffix/revision stamp is required.

Sharing is unaffected. The family member signs in to *their own* account, sees nothing of the
buyer's, and uses the code the buyer sent them — which is right: we must never hand someone else's
codes to whoever happens to sign in. They may save an importable individual code to their own
account from a typed-code build or the client area, which takes nothing away from the buyer. A
reseller CSV code has no portal record and remains local-only (§7, §9).

**Double-charging is prevented, not policed.** A credential the access server still accepts makes
the app premium, so a store purchase is not offered. A retained code marked `AccessExpired` or
`AccessCodeRejected` does not block **Restore Premium** from offering a purchase — and anything
that arrives paid anyway is accepted and surfaced rather than refused (above).

### Their serving credential is refused

**Only the access server can infer that an existing premium credential stopped working.** An
expiry displayed by the device or portal, a billing date, the device clock, a timeout, maintenance,
an unreachable portal or a failed refresh cannot make that decision. The passive ending flow starts
only when a connection reaches the access server and returns `AccessExpired` or
`AccessCodeRejected` for the serving credential. Other connection errors do not mean premium ended.

The two refusal codes make the current code a good candidate for repair or replacement, but neither
deletes it:

1. **Retain and mark it.** Keep a locally typed code on the device and keep an imported code in the
   account's one slot. Record the refusal reason and observation time. Say *expired* only for
   `AccessExpired`; describe `AccessCodeRejected` as a rejection. Persist the mark with the code's
   identity on every launch. It becomes effective again only after a retry succeeds.
2. **Resolve the account before showing stale news.** Refresh the account first. A newly active
   subscription outranks the refused code and is tried. A meaningful account change — a purchase
   or renewal — may justify one guarded retry of the same underlying code because that act may have
   extended it. A website code bought through **Restore Premium** is already the new deliberate
   selection and is tried instead. Otherwise a different deliberate account selection may be
   tried. The portal never guesses another purchased code merely because a date passed.
3. **Success repairs without a fork.** A successful connection clears the refused mark and returns
   directly to premium. At most, leave a durable explanation on the account page; do not block a
   person who just bought premium with the old code's refusal.
4. **Failure shows the concrete recovery actions.** Stop claiming that the refused credential is
   currently valid. Present **Retry/Restore**, **Change code** where the build supports it, and
   **Remove access code** where the credential is a removable local or imported code. Do not offer
   **Switch to the free version**: some builds have no free edition, and removal already names the
   actual state change. If the person removes an account-saved code, the app performs the same
   account-wide null Set described above. If they retain it, Restore may retry it, refresh the account,
   renew, buy or accept a new code as the build permits.
5. **Revival proves itself by connection.** If the issuer later extends the retained code, a
   successful retry returns to premium. When a repairing subscription later stops serving, the
   retained imported code may be tried again; a new refusal is a new ending and may be announced
   again.

Without a prior access-server refusal, no response is not a refusal and the last effective edition
state is preserved. After a refusal, a failed portal refresh cannot undo what the access server
already said; retry and removal remain available. There is no separate Premium Ended edition state
or `IsFreeEditionChosen` setting. With no effective credential, a build that has free service may
behave as free and a build that does not may show no access; that distinction belongs to build
capabilities, not account lifecycle.

**A refused code followed by a subscription is therefore unambiguous:** on return or foreground,
refresh the account before presenting the old refusal, try the subscription credential, and return
directly to premium when it succeeds. The refused imported code stays in its one slot, inactive and
available for Replace or account-wide Remove. It is not deleted merely because the subscription repaired access.
For a website code rather than an account subscription, entering checkout through **Restore
Premium** carries the same repair intent: the newly provisioned code becomes the deliberate
selection and is tried before the old refusal is shown. An ordinary second website purchase still
leaves the existing selection alone for gifting.

### They sign in with a different address than they bought with

This is not an edge case, and it cannot be designed away at the identity layer. Two causes make it
structural: a person buying with a work address and signing in with a personal one, and
**Hide My Email** — Apple offers a private relay address on every sign-in, and a relay address can
never match a purchase made before it existed. On iOS we must offer Apple sign-in once we offer any
other, and the relay is the person's choice within it.

A relay address is **pseudonymous, not anonymous**: it forwards to their real inbox, so password
recovery, support and invoices all reach them. What we lose is the *link* to an earlier purchase,
not the ability to reach the person.

Seven rules, and one case deliberately left to support:

1. **Sign in as the website account — the route that removes the problem.** A website buyer can sign
   in with the credentials they bought with, on any platform (§7). Then there is no mismatch to
   resolve: the buying identity and the sign-in identity are the same account by construction. This
   is the answer to give first, because every rule below is repair work and this one avoids the
   damage.
2. **Import the code — the route for a bearer code, from the app or client area.** Possession of the
   exact code is the proof, and it is the only proof someone behind a relay address can give — and a
   stronger one than an address, which anyone can type. The account **holds the string**, never the
   ownership of the billing behind it.

   Typing in the app is a local act first: the device uses the code at once, and the account hears
   about it afterwards, in that order. There is one door — the profile — and no second question:
   signed in, the app uploads it by itself; signed out, it simply stays local. The upload takes any
   well-formed code on trust, so a promotional, admin-issued, partner, access-manager-issued or
   reseller CSV code is saved exactly like a code this portal sold. Whether it *works* is the access
   server's verdict at connect time, which is the only place that verdict has ever belonged.

   **Uploading is not redeeming, and the difference carries the whole design.** Nothing is consumed:
   the code is untouched, keeps its own clock, and goes on working for everyone already using it.
   The same code may be uploaded into **any number of accounts**, anywhere, and nothing is taken
   from whoever uploaded it before — because the device limit rides on the code, not on the account
   (§2). Each account has exactly one upload slot. Uploading the same code is idempotent; uploading
   another replaces what is there, which is the accepted price of a single slot, and nothing is ever
   evicted by policy or expiry.

   A user-facing label may still read *redeem*, since that is the word people expect from a code
   box, but it must never behave like one. A code that could be used up, or held exclusively by the
   first account to enter it, would destroy gifting and family sharing at a stroke — and would break
   §7, where a person coming back after deletion enters the very code their old account pointed at.
3. **Rename when the address is free.** If they later reveal their real address and nothing else
   uses it, the account is simply renamed. No merge, no risk.
4. **Link when it is not.** The login and the customer record are separate things, and one login can
   own several customer records. Both are attached to the one login, and the person signs in once
   and sees both. Nothing is moved.
5. **Invoices never move.** Each customer record keeps its own history exactly as issued (§5).
   Linking is what lets one person see everything without altering a single document — which is the
   whole reason it beats merging.
6. **Identity is recognised by the provider and its subject, not by the address.** That is what keeps
   an account stable when a provider changes the address it sends. A *new* provider joins an existing
   account by matching a verified address — and this is exactly what a relay address defeats, so
   signing in with Apple-plus-relay after having a Google account produces a **second account**.
7. **There is no selection to change.** What serves is recomputed on every read from what the
   account holds (§8), so uploading a code adds a candidate rather than moving a pointer, and the
   client area decides only whether a code may be picked at all — never which one is picked now.

**One case is left alone on purpose: both accounts hold a live store subscription.** They are two
accounts, each genuinely served, and nothing merges automatically (below). The person may well be
paying twice; each purchase serves its own store's builds, each client area shows what its account
holds, and the checkout warning and the purchase notice (above) are what surface it. Support
unwinds it when asked — by moving a pointer, never by merging.

**How much actually breaks, by case:**

| They bought | What happens |
|---|---|
| In a store | **Nothing breaks.** Recovery asks the store, not us, so the address is irrelevant (§7) |
| On our website | **Nothing breaks if they sign in as their website account** (rule 1). If they signed in some other way, the link is lost until they import the code |
| Under another sign-in provider | The account splits, but whatever granted premium is recoverable by one of the routes above |

So premium always survives; what does not is account *continuity*, and rules 2 and 3 repair that
afterwards.

**Never build automatic account merging.** It is the most error-prone operation in any billing
system and it cannot be undone — services, invoices, balances and payment agreements must all agree,
and one mistake charges the wrong person. Moving a pointer achieves nearly all of it at none of the
risk. A true merge, if ever needed, is an administrator looking at both accounts.

**What stays manual.** Someone who has lost their code, signed in with a different address and
cannot reach the portal has nothing left to identify them by. That residue is accepted deliberately:
the alternative is building an identity system to solve a problem the person can solve by keeping
their code.

### They cannot reach us at all, because we are blocked where they are

This is a VPN, so a share of our users are in exactly the places where our own servers are hardest
to reach. **The portal can be blocked while the tunnel still works**, and that produces the one
deadlock in this document: signing in needs the portal, and reaching the portal needs a connection.

Nobody is stranded, because the app is useful before anyone signs in (§3):

1. **Connect first, on the free or trial path.** Premium is not required to open a tunnel, and that
   is what the trial is for — a person with no account and no code still has a way through.
2. **Then sign in, through the tunnel.** With the connection up, the portal is reachable like any
   other site, and everything in §7 and §8 proceeds normally.
3. **Then it stays.** The code applied to the device remains the credential until the access server
   explicitly refuses it during a connection, with no portal clock deciding on its behalf. Losing
   the portal again afterwards costs them nothing.

**Point 3 is a promise, not a side effect, and it constrains the app: an unreachable portal must
never remove premium.** A refresh that cannot reach us keeps what it already holds. A displayed
expiry or a portal answer about billing may change what the account page says, but it does not infer
that the serving credential failed; only `AccessExpired` or `AccessCodeRejected` returned by the
access server during a connection starts the passive ending flow. Explicit sign-out, account
deletion and removal remain explicit acts with their own stated consequences. Confusing an outage
with a refusal would switch off paying customers in precisely the regions that most need us.

**A typed code is the stronger route here**, on a build that has one (§9). It needs no portal at
all, not even once: somebody who already holds a code is premium on a fresh install with no network
round-trip of any kind (§7). That is the case where losing the code box costs most — so on a build
without one, the connect-first route above is not a convenience, it is the only door, and it has to
keep working.

What genuinely waits for a reachable portal is anything that **changes what the account holds**:
signing in, buying, attaching or replacing the imported code, and choosing a purchased code (§8).
Those are exactly the moments when steps 1 and 2 are available again, so nothing is permanently
lost — but the app must not present the wait as a refusal, and must not offer a purchase it cannot
complete.

**A renewal is not on that list**, and this is the part worth being precise about, because it is
where most of the anxiety would otherwise sit. The portal and the access manager are different
servers: the portal handles accounts and money, and the access manager decides at connection time
whether a code still opens anything (§2). A renewal extends the same code rather than issuing a new
one (§2), and the access manager learns the new expiry from our side — so a subscription renewing
while the portal is unreachable keeps working with nothing asked of the app or the person. Only the
*display* can lag, which §5 already covers.

### Their subscription came from a different store than the app they are using

It still works. Premium follows the account across platforms, so a subscription bought on one store
opens premium in the app on another.

The only difference is cancelling: we can only offer a link to the store that actually billed them.
When the app cannot open that store, it says so in neutral words instead of pointing at the wrong
place.

### They want to delete, and their subscription came from a different store

Deletion works normally — there is one account regardless of who billed it. But the warning matters
more here: we cancel a subscription in **no** store (§8), and the store they must go to is the one
they bought from, which may not be the platform they are holding.

The code we mail on the way out (§5, step 3) matters more here too. Asking the store at sign-in only
works on the platform that sold it; a kept code works anywhere, so for a cross-platform buyer it is
the route most likely to be available to them — provided their build can take one (§9).

### They want to delete, and they bought on our website

**Deletion goes ahead. A website purchase never blocks it.** A refusal here would force an unfair
choice — cancel a code you already paid for, or keep an account you want gone — and a deletion you
must wait a year for is not a deletion the stores accept. The only thing a refusal ever protected
was a card charge being orphaned behind an erased person, and cancelling the billing does that
directly.

1. **Cancel the billing, do not terminate the service.** Every website-billed service is set to
   cancel at the **end of its paid period**, so no renewal invoice is ever generated and the code
   still runs out the time it was bought for. Immediate termination would destroy something they
   paid for, which is the one thing we never do.
2. **Warn on the screen, mail the codes** (§5, steps 2 and 3). The confirmation says only that any
   code linked to the account will be gone and we will not be able to find it again; the codes
   themselves go to their address, with the same warning, before it is erased. Then delete. This is
   not special treatment for website buyers — a store subscription's code is mailed the same way,
   for the same reason.
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

Rule 2 is what makes this fair. The policy is sound — a bearer code is theirs to keep safe, and
whether they kept it is not our business — but it has to be *said once, at the only moment it
matters*, and the code itself has to arrive somewhere they can still find it. Without both, every
case becomes a support request we can never resolve, because the link between the person and the
code is exactly what deletion destroys.

**Why nothing needs to block.** Most gateways charge only when WHMCS asks them to, so cancelling the
billing ends it outright. A gateway that keeps its own schedule can still send one more charge — and
rule 5 catches it: refund, alert, cancel. Every gateway lets a merchant end an agreement from its own
dashboard, and many expose it through an API that WHMCS can call where the module supports it. So an
un-cancellable gateway is a tooling gap to close, never a reason to trap someone in an account.

**Their codes are untouched on our side — but they leave the person's own devices with the
account.** The codes those services delivered were sold outright, and they keep running to the end
of what was paid for: a friend already using one notices nothing. On the buyer's own devices,
though, the code was applied *by the account*, so it goes when the account goes — the device that
deleted drops premium like every other signed-in device (§5, §8). The mailed codes are the way
back: type one in where the build takes codes and optionally save it to the new account, or import
it through the client area for a codeless build (§7, §9). We take back what the account applied,
never what the person bought — and what the person bought is in their inbox.

### They delete while the subscription is between payments

Two states a store puts a subscription into when a payment fails. **Grace**: the store is retrying,
and access continues. **Hold**: the retries failed, access has stopped, but the subscription is still
open and can come back to life for about a month if they fix their card.

Deleting in either state is an **ordinary deletion**. Nothing special happens, and nothing is
blocked.

The subscription belongs to the store and stays in whatever state it is in. The code's expiry is
untouched either way — it runs on the store's clock, not ours. In grace it is still valid; in hold it
has already run out on its own. If the subscription recovers later, that is a renewal arriving after
deletion, which §7 already covers: the entitlement stays alive for recovery and the person is never
brought back.

**The one thing that changes is the warning, and it gets stronger rather than weaker.** Someone in
hold has already lost access, so the subscription looks finished to them. They delete believing there
is nothing left to cancel, then fix their card for some unrelated reason, the subscription revives,
and they are charged for a service attached to an account that no longer exists. This is the single
most likely way for someone to be billed after deleting, so it is the case where saying nothing costs
the most.

### They want us to stop the payments, not just the account

**We can always stop the service. Whether we stop the money depends on who is holding it**, and the
answer is fixed per channel — it is never a question put to the person:

| | Stop their service | Stop their billing |
|---|---|---|
| **The portal store (our website)** | Ours | **Always cancelled.** At end of period, no choice offered — the deleted account *is* this store's account (§2) |
| **An outside store** | Ours | **We do not.** Only the person can, in that store |

So the rule, and it is two lines rather than three:

1. **Website billing is always cancelled at the end of its paid period.** Nobody is asked. Somebody
   deleting their account is leaving, and nothing they paid for is lost — the code runs out the time
   it was bought for either way (§8, *they bought on our website*). An unpaid renewal that would
   otherwise be generated is exactly the orphaned charge deletion is supposed to prevent.
2. **A store subscription is left exactly as it is.** We do not cancel it even on the store that
   would let us.

**Why we do not offer to cancel a store subscription**, even where the store permits it. Because
signing in again brings it back by itself (§7). The store is asked at every sign-in and hands the
entitlement over silently, so a subscription that survives deletion is not a loose end — it is the
thing that makes coming back work. Cancelling it on the way out would quietly destroy the very asset
we would otherwise return, in exchange for a saving the person can make themselves in two taps in
their store.

Offering it would also mean offering something honest on one platform and impossible on the other:
either we explain the difference — naming stores, which we may not do (§10) — or we show a control
that silently does nothing. Neither is acceptable. **One rule that is true everywhere beats a
correct rule the person cannot be told.**

**Never claim to have done something we did not.** The confirmation says plainly that the
subscription is not cancelled and where to cancel it, in words that name no store.

**Why not something cleverer.** A store that takes the money before telling us leaves no moment to
refuse a renewal — the payment has already happened by the time we hear about it. Withdrawing a whole
plan from sale does stop renewals, but it stops them for **every** subscriber at once, which is a
tool for retiring a product rather than for one person. So the honest levers are the two above, and
a design that assumed any more would break on the platform that gives us least.

**Ending someone's service for abuse works the same way.** The code is revoked immediately — that is
always ours — and where the billing cannot be stopped, they are told plainly to cancel it themselves.
Revoking in silence while the charges continue is the worst option available: it invites a payment
dispute we would deserve to lose, and refunds are deducted from what the store owes us anyway.

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

**Refunding money does not switch a code off.** A code stops on its expiry date, and a refund is not
an expiry date — so unless someone ends the code, a refunded customer keeps working service until the
period they were refunded for runs out. Two deliberate outcomes, and the merchant picks:

| | When | What happens to the code |
|---|---|---|
| **Refund and revoke** | The normal case, and the default: the sale is being undone | Ended, so the money and the service go back together |
| **Refund and keep** | A goodwill gesture — an apology, a partial refund, a customer we want to keep | Left running to its original expiry, on purpose |

Both are legitimate; what must never happen is the second one **by accident**. So revoking is the
default and keeping it is the deliberate choice, never the other way round.

A store-issued refund is the store's decision, not ours, and it arrives as a notification — the code
is ended when the store says the entitlement is gone. **Refund and keep is a website-side option
only**, because only there are we the merchant.

## 9. Where a person can do it, and what each store allows

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

**This is also why deletion may never be refused into the web client area.** A refusal that says
*cancel your web services first, then delete* sends someone to a website to finish deleting — the
precise pattern the rule exists to prevent, and for a customer whose account began on the website
it would be the normal path, not an edge case. §8 cancels the billing outright instead, so the
in-app deletion always completes.

### Typing a code is not allowed everywhere

The two stores disagree about the one mechanism this document leans on most, and the disagreement is
not a grey area on either side.

**Apple forbids it.** Guideline 3.1.1 says an app may not use *its own mechanisms to unlock content
or functionality, such as license keys, augmented reality markers, QR codes* — a premium code is a
license key by any reading. Guideline 3.1.3(b) does permit an app to let people **reach** what they
bought on our website, but it permits the *access*, not the *mechanism*: the route Apple accepts is
signing in, where the server decides what the person owns and the app unlocks nothing by itself.
Apps have been refused on exactly our shape, including a free VPN client whose only paid keys came
from elsewhere. The US-storefront changes of 2025 do not help — they lifted the rules about linking
out to other ways of paying, and left this sentence untouched.

**Google permits it.** Play's payments policy has no equivalent sentence. It requires Play billing
for purchases made *inside* the app, and explicitly allows an app to be consumption-only — someone
logs in, or otherwise brings in what they paid for elsewhere. Since late 2025 it does not require
Play billing at all for US users. Nothing there is troubled by a code box.

Five consequences, and they are the reason several sections above are written the way they are:

1. **The code box is a capability of the build, not a check on the operating system.** It is
   configured per app, the way every other optional capability is. Writing it as *if this is
   platform X* would be wrong twice over: the same platform can carry a build that is allowed one
   (sideloaded, or distributed by us), and the rule can change on either store without the platform
   changing at all.
2. **What is forbidden is typing a code in, not reading the one they hold.** Apple's sentence is
   about *unlocking* with a license key. Nothing there stops a build from showing the buyer the code
   their own in-app purchase produced — and it must, because that code is how the same person is
   premium on their Android or Windows device, where typing it is allowed. So a build with no code
   box still shows the code, masked until they press to reveal it. Two separate permissions:
   *importing* is the build's capability, *viewing* is only the operator's policy.
3. **Signing in to the website account must exist on every platform** (§7). It is the only route
   from a website purchase into a build that cannot take a code, and it is the mechanism Apple's own
   3.1.3(b) points at.
4. **A bearer code needs a home that is not the app.** Someone handed a code by a friend has no
   website account and, on a codeless build, nowhere to type it. They **import** it into an account
   of their own in the client area, then sign in. Importing consumes nothing and may be done from
   any number of accounts (§8), so the friend who gave the code loses nothing by it. Without that
   page, a codeless build simply loses gifting.
5. **The client area must let a person name their code** (§8). Typing a code is how everyone else
   says *that is the wrong one*; a build that cannot take one has no in-app answer at all, so the
   website has to hold the only picker there is. This is the single piece of code management that
   survives, and it lives on the website precisely because the store does not govern it.

The wording rule is separate and stands on its own: **compare stores, never name one.** Anything the
person reads inside the app — including everything in §10 — must be true on every platform without
naming a competitor's store, because the same app ships to all of them.

## 10. What we promise before they confirm

The confirmation must carry the whole contract in a few lines, in store-neutral words — it ships in
one app on every platform, and naming a competing store is itself a store violation (§9). It must
say:

- **This cannot be undone.** The account and personal data are permanently deleted.
- Every signed-in device is signed out and loses premium.
- Any premium code linked to the account will be gone — from their own devices too — **and we will
  not be able to find it for them again**; the farewell mail is the only copy. A code in someone
  else's hands keeps working for them until it expires.
- A subscription bought in the app is **not cancelled by this**. Signing in again brings it back on
  the new account; cancelling it is done in their store's own settings. A subscription whose payment
  has failed is **still open** and can start charging again — said plainly, because it is the case
  they are least likely to expect.
- Invoices are kept for legal reasons.

**It shows no codes and no counts** (§5) — a promise to forget someone is also a promise to stop
being able to help them, and the warning above is where that is said. The codes themselves go by mail
instead, which is where they are still findable a year later.

**It offers to cancel nothing**, because we do not (§8). A control that silently does nothing
on the platform the person happens to be holding is worse than one honest sentence.

**It is confirmed by an explicit acknowledgement, not by answering a question.** A tick box reading
*I understand*, and a button that names the act. Never a Yes/No pair: yes and no are read as fast as
they are tapped, whereas a box that must be ticked and a button reading *Delete account* cannot be
completed absent-mindedly — and that is what a reviewer expects to find guarding a destructive act.

The privacy policy and the public deletion page must say the same thing. If any of the three
disagree, the one that promises the most is the one we are held to.

## 11. Open questions

**None at the moment.** Everything in this document has been decided.

New ones belong here rather than in the section they affect, so that a reader can tell at a glance
what is settled and what is not — and so that nobody assumes a behaviour that has never been agreed.
They are referred to elsewhere by their title, never by number, so answering one and removing it
never renumbers the rest.

### Answered elsewhere

These came up and are now settled — kept here only so they are not re-opened.

| Question | Answer |
|---|---|
| What brings a returning person back to premium? | Signing in — the store is asked automatically, and a website buyer signs in as their website account. The code we mailed them is the third route — §7 |
| Can two accounts hold the same code? | Yes, harmlessly — the device limit rides on the code, so there is nothing to fight over — §2, §7 |
| Could delete-and-recreate farm duplicate services? | No. The same purchase always returns the same code — §2 |
| Does a refund switch a code off? | Only if someone ends it. Revoking is the default; keeping it is a deliberate goodwill choice — §8 |
| Does a website purchase block deletion? | No. Billing is cancelled instead — §8 |
| Should a one-time purchase block it? | No. Nothing blocks it — §8 |
| Can a merchant end a gateway agreement? | Always, from the gateway's own dashboard; an administrator does it when alerted — §8 |
| Who pays for a refunded stray charge? | We do, once — which is why it comes with a cancellation, so it can never repeat — §8 |
| A renewal arriving after deletion | Recorded, the entitlement stays alive for later recovery, and the person is never resurrected — §7 |
| Does the app help someone holding several codes? | It does not need to. The server hands it one code, so the app never sees a list and never asks — §8 |
| How many uploaded codes can one account hold? | One. Uploading another replaces it; purchased services are separate inventory — §8 |
| Who chooses which code serves the account? | Nobody — it is ranked on every read: whatever is being paid for right now, then the other portal codes known to be valid, then the imported one. Deterministic, no dates. Nothing is stored as the selection, so nothing goes stale — §8 |
| Why did expiry stop promoting the next code? | Promotion shipped and was reversed (it could spend an unstarted prepaid code), and its replacement — a stored last deliberate choice — was reversed in turn, because a dead choice had to be repaired by hand on every device. The ranking has neither problem — §8 |
| Does an ordinary subscriber ever manage codes? | No. What they are paying for is ranked first, and there is no prompt at all: typing a code IS choosing to use it — §8 |
| Does the app show the person their codes? | No inventory crosses to the app. The client area lists purchased services and the one uploaded code; deletion still mails the purchased ones before erasing the account — §5, §8 |
| Can a premium code be typed into every build? | No. One store forbids unlocking with a code at all, so it is a per-build capability, ANDed with the token's own policy — §9 |
| Where does someone change which code serves them? | They type a code — into the profile, the one door, signed in or out. Signed in the app uploads it by itself. Which code then serves is the ranking's answer, not theirs — §8, §9 |
| Can the account's uploaded code be removed in the app? | No. Signed out, Remove clears this device's own copy; signed in there is no Remove at all — the ranking replaces a dead code by itself, and emptying the account's slot is the client area's job — §7, §8 |
| Does an uploaded code come back in the farewell mail? | No. Only what they bought here is mailed; an uploaded code is still theirs wherever it reached them from, and the deletion dialog says so — §5, §8 |
| Do we refuse a store purchase to an existing website customer? | We prevent it: checkout never opens while the account is served, checked with the server before the store's payment sheet. Nothing is refused after payment — §8 |
| What if the store places the order anyway? | It is accepted and provisioned — the account holds both, the client area shows both, and a message says where each is cancelled — §8 |
| Why not refuse and let the store refund? | Only one store refunds an unacknowledged purchase by itself; the other keeps the buyer's money and gives us no way to return it. Prevention costs nobody anything — §8 |
| Which purchase serves a device? | Its home store's subscription while it is serving — grace counts, hold does not — otherwise the account's selected code — §8 |
| How does someone switch stores? | Cancel at the store that billed, let the paid time run out, subscribe at the new one. Nothing moves a subscription between stores — §8 |
| Should invoices be anonymised? | No. They keep the buyer's name, frozen as issued and locked out of ordinary use — §5 |
| How many years do we keep an invoice? | We do not publish a figure. The policy names the legal obligation as the criterion, which is what the law asks for and what our market does — §5 |
| What if they sign in with a different address than they bought with? | Sign in as the website account instead; failing that, import the code — then rename the account when the address is free, link the records when it is not — §8 |
| Does entering a code use it up, or tie it to one account? | Neither. It creates a pointer, consumes nothing, and the same code may be imported into any number of accounts — §2, §8 |
| What if our portal is blocked where they are? | Connect on the free or trial path first, then sign in through the tunnel; the applied code stays until the access server explicitly refuses it during a connection — §8 |
| Can an unreachable portal drop someone's premium? | Never. Without `AccessExpired` or `AccessCodeRejected`, an outage preserves the last effective edition state. Explicit sign-out, deletion and removal keep their stated consequences — §5, §8 |
| What passively ends premium? | Only `AccessExpired` or `AccessCodeRejected` returned by the access server during a connection. A displayed date or failed refresh does not — §8 |
| Somebody types a code while their account already holds one — which is used? | The one they typed. Typing a code means *use this one*, so it outranks every code nobody is being billed for, including ones we sold them. The old code is not consumed; it waits and is served again if the typed one stops working — §8 |
| …and if they have a live subscription? | The subscription still wins. The typed code is saved and connects on the spot, but the next account read hands the subscription's code back and the device follows it: a fresh code is never spent on top of something they are already paying for. It is served automatically once the subscription ends, or right away if they sign out and enter it on the device — §8 |
| Every code somebody holds is refused — do they get nothing? | No. A refusal pushes a code below every working one, but it is never taken away: once they have all been refused the codes take turns, least recently refused first, so the account always hands one back and they get the same honest error until one works, they replace them, or they sign out. It is also how a topped-up code returns by itself, and the app takes one turn per press — §8 |
| A subscription ends — what happens to its code? | It stops being one of their codes on the next read. We ended it and we know when, so nothing waits for a refusal; their devices fall through to whatever else they hold, or are told they have nothing. Cancelling is not ending: the period already paid for is theirs — §8 |
| A PAYING person's code is refused — do we give them another? | No. They are paid up and the credential we provisioned does not work, so support fixes it at the source. Substituting another code they hold would spend a code they were saving and hide our own fault. The app says the device could not be given access, never that their access ended — §8 |
| What happens after that refusal? | Repair first; if nothing succeeds, retain the code and offer Retry/Restore, Change code where supported, and Remove access code where removable. There is no separate free-edition choice — §8 |
| Can restarting revive a refused typed code above a working account code? | No. The refused mark is persisted with that code and participates in precedence until a retry permitted by the refusal flow succeeds — §8 |
| They buy premium after a refusal — show the old error? | No. Resolve the changed account first and try the subscription or repair purchase. Success returns directly to premium; the refused import stays stored — §8 |
| Removing the account's imported code — ask twice? | No. One confirmation names the account-wide removal; the resulting UI follows the build's remaining capabilities — §8 |
| What do the account's other devices do after removal? | Apply the empty account slot on their next successful refresh. They do not ask for the account decision again; offline devices cannot be changed without revoking the shared bearer code itself — §8 |
| Does sign-out need an edition warning? | No. Sign-out removes account-applied state only from this device and does not mutate the account. Its normal confirmation is sufficient — §8 |
| Does a renewal need the portal? | No. It extends the same code, and the access manager — a different server — honours the new expiry without the app being told — §2, §8 |
| Do we ever merge two accounts? | No, never automatically. A pointer moves; customer records and invoices never do — §8 |
| How many devices may share a code? | The access manager's policy, not ours. It counts installations pseudonymously and this document never depends on the number — §2 |
| Should the website limit how many one person may buy? | No. A warning at checkout instead, and it never blocks — §8 |
| Does a reseller's stock show up as their own code? | No. Stock is never offered as a personal code and never becomes a default — §8 |
| Does suspending a bulk order stop the codes? | No. An administrator disables them by hand, and the system says so rather than claiming success — §8 |
| Deleting while a payment has failed — special case? | No, an ordinary deletion. Only the warning changes, and it gets stronger — §8 |
| Can we stop a store charging them? | We do not try, on any store. Signing in again gives the subscription back, so cancelling it on the way out would destroy what we would otherwise return — §8 |
| Can we refuse a renewal as it happens? | No. The money moves before we are told. Cancelling beforehand is the only lever — §8 |
| A device that never comes back online | Not a leak. A code only acts at the moment of connecting, and connecting is the check — §5 |
| Does deletion erase our connection logs? | No, and it does not need to. They run out their own 30 days and expire — §5, §6 |
| Can a deletion destroy records under a preservation request? | No. A legal hold suspends expiry until the matter is resolved, and it outranks every retention rule here — §5 |
| Is a bulk order revocable? | Yes, by an administrator, by hand. The system refuses loudly rather than pretending it worked. Automating it is not worth the volume — §8 |
| When does a code start counting down? | A prepaid one-time code on first use; anything billed on a cycle expires with the cycle — §4 |

## 12. Where the wording lives

Three places must agree, and all three are translated:

- The confirmation the app shows before deleting.
- The **Delete Your Account** section of the CONNECT privacy policy.
- The public account-deletion page on our website.

Change one, change all three. The English is authored once and every other language is generated
from it.
