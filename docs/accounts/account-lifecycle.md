# Account lifecycle — business flow

*Last reviewed: 2026-08-13*

The life of a VpnHood! account in business terms: when one comes into existence, what it holds while
it lives, what happens when someone deletes it, and how a paying customer gets back what they bought.
No implementation detail — this is the behaviour a support agent, a store reviewer or a lawyer needs
to be able to predict, and the reference the in-app wording and the privacy policy must agree with.

Applies to **VpnHood! CONNECT**, the only app with sign-in today.

> **Two kinds of content, and one of them is temporary.** The numbered sections describe the
> business and are permanent. Anything describing *what the software does today* is scaffolding for
> the build and is **deleted once the work is done** — it is confined to **§12** and to the
> indented notes that begin **Decided**, followed by a date, so it can be removed in one pass
> without touching a line of the business rules. A merchant reading this after that point should
> find no mention of implementation at all.

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
12. [Decided, but not built yet](#12-decided-but-not-built-yet)
13. [Where the wording lives](#13-where-the-wording-lives)

Section 8 answers these, in order:

- They already have a subscription and try to buy again
- They order a second subscription on our website
- They bought on our website, then sign in to the app
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
| Who has an account? | Only someone who signed in — and the only reason to sign in is to buy | §3 |
| Website buyer signs in — premium? | **Not today** (they must paste the code). Decided: yes, automatically | §8 |
| Which key, if they own several? | The first one they bought; later purchases never steal that slot; several unclaimed → ask, never guess | §8 |
| Two subscriptions? | Any number on the website (for sharing); never more than one from a store | §8 |
| Is a key safe to hand out? | Yes — it carries its own device limit, whoever holds it | §2 |
| What does deletion erase? | The person, on every device. Premium granted by the account dies with it | §5 |
| What does deletion keep? | Their store subscription, their website keys, and invoices frozen with the buyer's name | §6 |
| What blocks deletion? | **Nothing.** Billing is cancelled at end of period instead (today it still refuses — being changed) | §8 |
| Coming back? | A new, empty account — plus the code they saved on the way out, or the store asked at sign-in | §7 |
| Does a refund end a key? | Only if we end it. Revoking is the default; keeping it is a choice | §8 |

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

### Why the code can be handed around safely

A premium code **carries its own device limit**, and that limit is enforced wherever the code is
used, no matter which account — or how many accounts — hold it. Ten people with the same code still
get the number of devices that code was sold for.

This is the quiet foundation under most of the answers below. It is why we can show a code to
someone whose account is being erased, why a code can be pasted into a fresh install, and why nobody
gains anything by holding the same code twice. **Sharing a code was never the risk.**

**Who enforces it, and why nothing in this document decides it.** The limit is applied by the access
manager at connection time, under its own policy — how many may connect at once, and how a device is
counted. A device is recognised by a random identifier the app generates per installation, sent in a
form only a server holding that code can read: no account, no name, nothing that identifies a
person. The same device reconnecting does not consume a second place.

That policy is deliberately **not** settled here. It belongs to the access manager, it can change
without changing anything about accounts or purchases, and the app cannot see the count in any case.
What this document depends on is only the guarantee above — that a code cannot be stretched by
spreading it around — not the particular number or the way it is counted.

The risk it does *not* cover is **minting** — one purchase producing two *different* codes, which
would double the device limit. Hence the rule that matters:

> **One purchase, one code, for the life of that purchase.** Proving the same purchase again always
> returns the same code, never a new one. Renewals extend it; they never replace it.

Two other things the device limit does not do, so they are handled separately:

- **It does not limit time.** Every code must carry an expiry, and it stops when the paid period
  ends. Nothing may ever be issued without one.
- **It does not react to a refund.** A refund does not expire a code by itself — see §8.

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

### When a key starts counting down

Two different answers, and the difference is what was sold:

- **A prepaid one-time key starts on first use.** Nothing is set when it is bought, so a key bought
  in January and given away in June runs its full term from June. This is what makes a key a
  sensible gift.
- **Anything billed on a cycle expires with the cycle.** A subscription is paid for a calendar
  window — the store charged for March — so the key runs to the end of that window and is pushed
  forward each time it renews. It cannot start on first use: a cancelled subscription would then
  keep working past what was paid for, and anyone could park a subscription unused and stretch it
  indefinitely.

This holds the same way whether the subscription came from a store or from our website.

## 5. Deleting an account

The person taps **Delete my account** in the app, or does the same from the website client area.
Both do the same thing — there is only one account.

Before anything is erased, we tell them what it means (§10). Then, in this order:

1. **Stop the money first.** Every service bought on our website is cancelled at the end of its paid
   period, so no renewal invoice is ever generated and the key keeps working until the time they
   bought runs out. Unpaid invoices are cancelled; paid ones are kept, and the stored payment method
   is dropped. Nothing here refuses the deletion — see §8.
2. **Show them every key they paid for, one last time** — the keys from website purchases *and* the
   code behind a store subscription — with a plain warning: *these keep working; save them now,
   because after this we cannot show them to you again.* This is the last moment the link between
   the person and their keys exists. See below for why this is safe, and §7 for what it buys them.
3. **Send one final message to their address, before it is erased.** It carries the same keys and
   the same warning about any subscription still running. The confirmation screen is seen once and
   dismissed; an inbox is searchable a year later, which is when they will actually want the key.
   This is the last legitimate use of that address — confirming an action they just asked for — and
   it is not a new exposure, because a key bought on our website was delivered by mail in the first
   place.
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

**Why showing the codes is safe.** A code carries its own device limit (§2), so handing it back gives
away nothing beyond what was already bought, and it still expires when the paid period ends. It is
also the only thing that survives the erasure *usefully*: everything else we could keep to help them
later would be a record of a person we just promised to forget.

### Why invoices keep the buyer's name

> **Decided 2026-08-13 — reversing an earlier choice.** Invoices used to come out anonymous. They
> now keep the name they were issued with, and only the *person* is erased.

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

> **Decided 2026-08-13.** The wording is: *invoices are kept because tax law requires it, for as
> long as that law requires, and are used for nothing else.*

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

**One ordering rule remains.** The privacy policy and the public deletion page currently promise
anonymisation, in thirteen languages. They are corrected **before** the behaviour changes, never
after — a window in which we keep data our own published policy says we destroyed is worse than
either choice on its own.

Billing is stopped first and identity erased last, on purpose: a half-finished deletion must never
leave a live charge behind an account that no longer exists. If any step cannot be completed, the
whole thing aborts with a message rather than half-deleting.

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
does keep working is in exactly the position of someone who saved their key and entered it again,
which is what we now deliberately offer everyone on the way out (step 2 above). What would once have
looked like a gap is the behaviour we chose.

**So one rule carries all of this, and it is the one in §2: nothing may ever be issued without an
expiry.** Every other protection here is a convenience. That one is not, and it is why it is written
as an absolute.

One cosmetic consequence, which resolves itself: a device that has been offline may still show
premium in its own screens after the code behind it has expired, until it next reaches us or tries to
connect.

## 6. What deletion does **not** do

- **It does not cancel a subscription by itself.** Unless they ask us to and we are able to, the
  store keeps charging until they cancel it there. We say this before they confirm — see §8 for
  where we can offer to do it for them and where we cannot.
- **It does not refund anything.** Refunds are the store's decision for store purchases, and ours
  only for website purchases.
- **It does not erase invoices, and does not strip the name off them.** We are legally required to
  keep financial records, and a financial record has to say who bought. They are frozen as issued and
  locked out of ordinary use — see §5. This is disclosed, with the retention period, before anyone
  confirms a deletion.
- **It does not take back the keys they paid for.** They were shown them on the way out (§5), and
  those keys run to the end of the period they were bought for.
- **It does not reach into backups instantly.** Residual copies may remain for up to **30 days**,
  after which they expire.
- **It does not erase what other companies hold in their own right** — the sign-in provider, the
  store that billed, the payment processor. They keep their own records under their own policies.

## 7. Coming back afterwards

Signing in again creates a **brand-new, empty account**. The old one is not restored, and we keep no
way to recognise the returning person — that is deliberate.

What they paid for is still theirs, and there are **two ways back to it**. Neither requires us to
remember who they were.

### The code they saved — the main route

If they kept the code we showed them at deletion (§5, step 2), they simply **paste it in**. Nothing
else is needed: no account, no store, no network round-trip to prove anything. The app files it as
their own code, exactly as it treats a code from a gift or a promotion, and the device limit that
came with it still applies.

This works on a phone that has never seen their account, on a platform they did not buy from, and
years later. It is the reason step 2 of §5 exists — a person who saves their code can never be
locked out by anything we do afterwards.

### Asking the store — the convenience route

For everyone who did not save it, the app can prove the purchase to the store instead, and we match
that proof back to the code. **The same code comes back, not a new one** (§2) — otherwise every
delete-and-return would mint a fresh service.

> **Decided 2026-08-13 — how this should behave.** It happens **as part of signing in**, quietly,
> with nothing to tap: right after the session is established the app asks the store what this store
> account owns and presents anything we do not already know. A **visible restore control stays**
> alongside it — Apple expects to find one, and it is the way out when the store account signed in
> on the device is not the one that bought.
>
> Four conditions make it safe to put in the sign-in path:
>
> 1. It must be the **silent** kind of store query, the one that reads what the device already
>    knows. The older kind asks for the store password, and that must never happen on every sign-in.
> 2. Presenting a purchase we already know is a **no-op**, never a second purchase.
> 3. A failed store query **never fails the sign-in**. Offline just means not premium yet, and the
>    visible control is the retry.
> 4. Sign-in **never waits** for it. It completes, and premium appears when the answer arrives.

**No ownership fight is possible**, and this is worth stating plainly for anyone reviewing the
design: because the device limit rides on the code (§2), a code being reachable from more than one
account grants nobody an extra device. There is nothing to take away from a previous holder, and no
rule is needed about who wins.

## 8. Situations and answers

### They already have a subscription and try to buy again — including from a different store

**We refuse the purchase.** The subscription belongs to the account, not to the platform, so an
account that already holds one cannot buy a second. The app says they already have an active
subscription and sends them to their account page.

This is what stops someone paying twice by subscribing on a phone and then again on a tablet from a
different store.

#### It must be refused twice, in two different places

Not offering it is **prevention**; refusing it is **enforcement**. They are not the same job and
neither one covers for the other:

| Layer | What it does | Why it is not enough alone |
|---|---|---|
| **The app** | Never shows checkout to someone who is already premium | An old build, an interrupted flow or a race gets past it — and it can only see what *this* device knows |
| **Our server** | Refuses to turn the purchase into service | It is the last word, so it must be able to answer the question for the whole account, not one channel |

> **Decided 2026-08-13 — the server refuses a store purchase when the account already holds a live
> subscription from *either* channel: a store purchase, or a service bought on our website.** Today
> it only counts store purchases, so a website customer can be sold a store subscription on top of
> the one they already pay for.

**Refuse before provisioning, never after.** The order matters and is already right: the refusal
happens before the purchase is finalised with the store, so it is never acknowledged, and the store
**refunds it automatically**. The buyer is made whole without us ever holding money for a service
they cannot use, and without anyone having to notice and act. A refusal issued *after* provisioning
would leave us holding their money.

**Why this is worth doing even though "it is never offered".** Right now both layers are missing at
once. The app's prevention depends on a website customer being premium the moment they sign in — and
that behaviour is decided but **not yet built** (below). So today a website customer signing in on a
fresh device is not premium there, is offered a store subscription, and the server accepts it. The
two layers were meant to back each other up; neither is standing.

**One case this can catch wrongly.** A person is allowed to hold several website subscriptions on
purpose — for sharing (below). Someone who bought a key as a gift and then buys a store subscription
for themselves would be refused by this rule. That is a false positive, and an acceptable one: it
costs an annoyed customer and an automatic refund, while the false negative costs a real double
charge and a support case we have to unwind by hand.

If it turns out to bite real buyers, the refinement is already in the design and needs no new
machinery: block only when the existing subscription is the one **actually serving them** — their
default key (below) — so a key they bought for someone else does not stand in their way. Ship the
blunt rule; keep this in reserve.

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

#### Should there be a limit?

> **Decided 2026-08-13 — no limit. A warning at checkout instead.**

A limit is the wrong tool. Nobody buys a fifth key by mistake; they buy a *second* one, because they
forgot they had one or thought it had run out. A limit set at three does not prevent that, and a
limit set at one destroys the product. It would also break the two cases the website exists to
serve — buying for the family, and selling in bulk.

The accident is worth preventing, but it is a **confirmation** problem, not a quantity problem. So
when someone who already holds something active reaches checkout, they are told what they have
before they pay, and the wording depends on **whether what they hold will continue on its own**:

| What they already hold | Buying again is | What they are told |
|---|---|---|
| A subscription that renews itself | Usually a mistake | It renews on its own, and a second purchase is a separate key, not an extension |
| A key they have never used | Almost certainly a mistake | They have a key they have not used yet |
| A key in use with time left | Possibly deliberate — a gift, or family | What they hold, and when it runs out |
| A key expiring or already expired | **The correct action** | Nothing at all |

Three rules govern it:

1. **It never blocks.** One step past it, always. A warning that cannot be passed is a limit wearing
   a different hat.
2. **It stays silent when buying is right.** The last row matters as much as the first three: a
   warning that fires when the purchase is correct teaches people to dismiss it unread, and then it
   no longer works for the case it was built for.
3. **Where several are held, it describes the one that bears on the decision** — the longest-lived,
   or the one that renews itself. Never an arbitrary one. Telling someone about an unused key while
   their live service is days from lapsing is worse than saying nothing, because it suggests they
   are covered when they are about to lose it.

Bulk and reseller orders skip it entirely: there is no interactive checkout, and a warning repeated
fifty times is noise.

**One reason a limit gets proposed that it cannot solve.** An unlimited quantity field is a fraud
amplifier — fifty keys bought on a stolen card and resold before the chargeback lands. That risk is
real, but a per-person limit does not touch it, because fraud uses fifty accounts rather than one.
Order velocity limits and manual review above a value threshold are the tools for it, and they
belong with the payment controls, not here.

The consequence for deletion is that "cancel your website subscription first" means **all** of them.

### They bought in bulk, to resell

> **Decided 2026-08-13.** Bulk selling is a separate product, offered only to merchants we choose,
> and it behaves differently from every other sale in this document.

A bulk order is **stock, not service**. Someone buying fifty keys is buying inventory to sell, not
fifty things to use. Three consequences follow, and all of them are deliberate:

1. **The keys are delivered as a file**, once, at purchase. There is no single code to look up
   afterwards, so the client area shows the delivery rather than a code.
2. **Stock is never treated as the buyer's own key.** It is never offered in the app as "your key"
   and never becomes their default. A reseller who wants to use one of their own keys enters it by
   hand, like anyone else holding a code.
3. **Suspending or cancelling a bulk order does not switch the keys off by itself.** They were
   handed over as a file and they keep working. **An administrator disables them directly, and the
   system says so loudly** — it refuses the operation with a message naming the batch, rather than
   reporting success for something that did not happen. Doing it by hand is the decision, not a
   limitation: automating it would mean tracking every key in every batch, and the volume does not
   justify that while bulk delivery goes only to merchants we have chosen.

Point 3 is the one that matters commercially, so it is worth stating plainly: **a reseller who takes
delivery and then does not pay keeps working keys until someone acts by hand.** That is a deliberate
trade — bulk delivery is offered only to merchants we have chosen, and choosing them is the control.
It is not something the billing system enforces on our behalf.

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

### They sign in with a different address than they bought with

> **Decided 2026-08-13.** This is not an edge case, and it cannot be designed away at the identity
> layer. Two causes make it structural: a person buying with a work address and signing in with a
> personal one, and **Hide My Email** — Apple offers a private relay address on every sign-in, and a
> relay address can never match a purchase made before it existed. On iOS we must offer Apple
> sign-in once we offer any other, and the relay is the person's choice within it.

A relay address is **pseudonymous, not anonymous**: it forwards to their real inbox, so password
recovery, support and invoices all reach them. What we lose is the *link* to an earlier purchase,
not the ability to reach the person.

Six rules, and one refusal:

1. **Claim by code — the main route.** They paste their key once, and the account records a pointer
   to it. Possession of the code is the proof, and it is the only proof someone behind a relay
   address can give. It is also a stronger proof than an address, which anyone can type.
2. **Rename when the address is free.** If they later reveal their real address and nothing else
   uses it, the account is simply renamed. No merge, no risk.
3. **Link when it is not.** The login and the customer record are separate things, and one login can
   own several customer records. Both are attached to the one login, and the person signs in once
   and sees both. Nothing is moved.
4. **Invoices never move.** Each customer record keeps its own history exactly as issued (§5).
   Linking is what lets one person see everything without altering a single document — which is the
   whole reason it beats merging.
5. **Identity is recognised by the provider and its subject, not by the address.** That is what keeps
   an account stable when a provider changes the address it sends. A *new* provider joins an existing
   account by matching a verified address — and this is exactly what a relay address defeats, so
   signing in with Apple-plus-relay after having a Google account produces a **second account**.
6. **Last-one-wins applies only to deliberate acts** — pasting a code, or choosing a default in the
   portal. Never to automatic attachment, which must never overwrite a key that still works (above).

**The refusal: if both accounts hold an active *store* subscription, do not resolve it
automatically.** Both are being paid for, and one account may never hold two store subscriptions
(above). Merging them would break that rule and could cascade into a refusal or a refund. Tell the
person plainly that they are paying twice and let support unwind it — that is a real double charge,
and we want it surfaced rather than quietly absorbed.

**How much actually breaks, by case:**

| They bought | What happens |
|---|---|
| In a store | **Nothing breaks.** Recovery asks the store, not us, so the address is irrelevant (§7) |
| On our website | The link is lost. Claiming by code is the only route back |
| Under another sign-in provider | The account splits, but whatever granted premium is recoverable by one of the two routes above |

So premium always survives; what does not is account *continuity*, and rules 2 and 3 repair that
afterwards.

**Never build automatic account merging.** It is the most error-prone operation in any billing
system and it cannot be undone — services, invoices, balances and payment agreements must all agree,
and one mistake charges the wrong person. Moving a pointer achieves nearly all of it at none of the
risk. A true merge, if ever needed, is an administrator looking at both accounts.

**What stays manual.** Someone who has lost their key, signed in with a different address and cannot
reach the portal has nothing left to identify them by. That residue is accepted deliberately: the
alternative is building an identity system to solve a problem the person can solve by keeping their
key.

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

The code shown on the way out (§5) matters more here too. Asking the store only works on the
platform that sold it; a saved code works anywhere, so for a cross-platform buyer it is the route
that will actually be available to them.

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
2. **Show the keys one last time** (§5, step 2), with a plain warning: *these stay active; save them
   now, because after this we cannot show them to you again.* Then delete. This is not special
   treatment for website buyers — a store subscription's code is shown the same way, for the same
   reason.
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

### They delete while the subscription is between payments

Two states a store puts a subscription into when a payment fails. **Grace**: the store is retrying,
and access continues. **Hold**: the retries failed, access has stopped, but the subscription is still
open and can come back to life for about a month if they fix their card.

> **Decided 2026-08-13.** Deleting in either state is an **ordinary deletion**. Nothing special
> happens, and nothing is blocked.

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

**We can always stop the service. We cannot always stop the money.** Keeping those two apart is what
makes the rest of this section work:

| | Stop their service | Stop their billing |
|---|---|---|
| **Our website** | Ours | **Ours** — cancelled at end of period |
| **One store** | Ours | **Ours** — the subscription can be cancelled on our side, and stays valid to its expiry |
| **The other store** | Ours | **Not possible.** Only the person can cancel |

So the rule:

1. **Where we can cancel, offer it** — at deletion, as a plain choice, ticked by default, and act on
   it. Nothing they paid for is lost: a cancelled subscription still runs to the end of its period.
2. **Where we cannot, say so.** The option is simply not shown, and the warning carries the whole
   weight.
3. **Never claim to have done something we did not.** The person is told what actually happened, on
   whichever platform they are using.

**Why not something cleverer.** A store that takes the money before telling us leaves no moment to
refuse a renewal — the payment has already happened by the time we hear about it. Withdrawing a whole
plan from sale does stop renewals, but it stops them for **every** subscriber at once, which is a
tool for retiring a product rather than for one person. So the honest levers are the three above, and
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

**Refunding money does not switch a key off.** A key stops on its expiry date, and a refund is not
an expiry date — so unless someone ends the key, a refunded customer keeps working service until the
period they were refunded for runs out. Two deliberate outcomes, and the merchant picks:

| | When | What happens to the key |
|---|---|---|
| **Refund and revoke** | The normal case, and the default: the sale is being undone | Ended, so the money and the service go back together |
| **Refund and keep** | A goodwill gesture — an apology, a partial refund, a customer we want to keep | Left running to its original expiry, on purpose |

Both are legitimate; what must never happen is the second one **by accident**. So revoking is the
default and keeping it is the deliberate choice, never the other way round.

A store-issued refund is the store's decision, not ours, and it arrives as a notification — the key
is ended when the store says the entitlement is gone. **Refund and keep is a website-side option
only**, because only there are we the merchant.

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
- This does **not** cancel the subscription by itself — it belongs to the store where it was bought
  and may keep renewing until cancelled there. Where we are able to stop it for them, that is
  offered as a plain choice and they are told what actually happened.
- A subscription whose payment has failed is **still open** and can start charging again. This is
  said most clearly of all, because it is the case where they are least likely to expect it.
- While that subscription is still running, it can be brought back onto a new account.
- Invoices are kept for legal reasons.

And it must **show them their keys at that moment**, with the warning that this is the last time we
can (§5). A promise to forget someone is also a promise to stop being able to help them, and the
only honest place to say so is before they confirm — not in a support reply afterwards.

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
| What brings a returning person back to premium? | The code they saved, first; asking the store at sign-in, second — §7 |
| Can two accounts hold the same code? | Yes, harmlessly — the device limit rides on the code, so there is nothing to fight over — §2, §7 |
| Could delete-and-recreate farm duplicate services? | No. The same purchase always returns the same code — §2 |
| Does a refund switch a key off? | Only if someone ends it. Revoking is the default; keeping it is a deliberate goodwill choice — §8 |
| Does a website purchase block deletion? | No. Billing is cancelled instead — §8 |
| Should a one-time purchase block it? | No. Nothing blocks it — §8 |
| Can a merchant end a gateway agreement? | Always, from the gateway's own dashboard; an administrator does it when alerted — §8 |
| Who pays for a refunded stray charge? | We do, once — which is why it comes with a cancellation, so it can never repeat — §8 |
| A renewal arriving after deletion | Recorded, the entitlement stays alive for later recovery, and the person is never resurrected — §7 |
| Does the app help someone holding several codes? | Yes — §8 |
| Do we refuse a store purchase to an existing website customer? | Yes, twice: the app never offers it, and the server refuses it whichever channel the existing subscription came from — §8 |
| What if the store places the order anyway? | The server refuses before provisioning, so the purchase is never acknowledged and the store refunds it automatically — §8 |
| Should invoices be anonymised? | No. They keep the buyer's name, frozen as issued and locked out of ordinary use — §5 |
| How many years do we keep an invoice? | We do not publish a figure. The policy names the legal obligation as the criterion, which is what the law asks for and what our market does — §5 |
| What if they sign in with a different address than they bought with? | Claim by code; rename the account when the address is free, link the records when it is not — §8 |
| Do we ever merge two accounts? | No, never automatically. A pointer moves; customer records and invoices never do — §8 |
| How many devices may share a code? | The access manager's policy, not ours. It counts installations pseudonymously and this document never depends on the number — §2 |
| Should the website limit how many one person may buy? | No. A warning at checkout instead, and it never blocks — §8 |
| Does a reseller's stock show up as their own key? | No. Stock is never offered as a personal key and never becomes a default — §8 |
| Does suspending a bulk order stop the keys? | No. An administrator disables them by hand, and the system says so rather than claiming success — §8 |
| Deleting while a payment has failed — special case? | No, an ordinary deletion. Only the warning changes, and it gets stronger — §8 |
| Can we stop a store charging them? | On one store yes, offered as a choice at deletion; on the other only the person can — §8 |
| Can we refuse a renewal as it happens? | No. The money moves before we are told. Cancelling beforehand is the only lever — §8 |
| A device that never comes back online | Not a leak. A code only acts at the moment of connecting, and connecting is the check — §5 |
| Is a bulk order revocable? | Yes, by an administrator, by hand. The system refuses loudly rather than pretending it worked. Automating it is not worth the volume — §8 |
| When does a key start counting down? | A prepaid one-time key on first use; anything billed on a cycle expires with the cycle — §4 |

## 12. Decided, but not built yet

> **This whole section is temporary — delete it when the work is done.** It exists for the people
> building, not for the people the document is written for. Once every row is true of the software,
> this section and the dated notes elsewhere go, and what remains is a description of the business
> with nothing in it about how the software got there.

Everything above describes how the business **should** work. Several of those decisions are ahead of
the code, and they are collected here so nobody has to hunt for them section by section. Nothing on
this list is an open question — each one is settled; only the work is outstanding.

| What was decided | Where | What the code does today |
|---|---|---|
| A website buyer is premium the moment they sign in, on the key marked as theirs by default | §8 | Nothing — they must paste the code by hand |
| The first key bought becomes the default at purchase time | §8 | No default is recorded |
| The server refuses a store purchase when a live subscription exists in **either** channel | §8 | It counts store purchases only, so a website customer can be sold a second one |
| Deletion cancels website billing at end of period instead of refusing | §8 | Deletion is refused while any website service is active |
| Every key the person paid for is shown once, on the way out | §5, §10 | Nothing is shown; the link between person and key is simply destroyed |
| A returning person is offered their subscription back at sign-in, silently, with a visible control beside it | §7 | The matching exists on our side; nothing asks the store |
| A refund revokes the key by default, with *refund and keep* as a deliberate choice | §8 | A refund does neither — the key runs on to its original expiry |
| No service is ever issued without an expiry | §2 | Believed true; worth confirming rather than assuming |
| Invoices are frozen with the buyer's name; only the customer record is erased | §5 | Erasing the customer record strips the name off the invoices with it |
| Checkout warns someone who already holds something active, and never blocks | §8 | Nothing is said; a second purchase goes through in silence |
| A person signing in with a different address can claim their purchase with its code | §8 | No route exists; they become a second, unrelated customer |
| A bulk order is marked as stock when it is sold | §8 | Nothing marks it; it is only distinguishable by having no single key recorded |
| Suspend, cancel and renew on a bulk order report an error naming the batch, and change nothing | §8 | They send an incomplete request that silently does nothing, while the panel reports success |
| The client area shows a bulk order as a file delivery, not as a code | §8 | It tries to fetch a code that does not exist |
| Deletion offers "also stop future renewals" where the store allows it, and acts on it | §8 | No such option; the store is never asked to cancel |
| One final message goes to their address at deletion, carrying their keys and the warning | §5 | Nothing is sent; the confirmation screen is the only chance they get |
| The confirmation states that a subscription in grace or hold is still open and will charge again | §8, §10 | The warning does not vary, so the case most likely to cost them money reads like every other |

Two of these protect money directly — the double-sale refusal and the refund revoke — and two
protect the promise we make to the person: showing the keys before erasing them, and giving them a
way back afterwards.

### Defects found while writing this

These are not decisions waiting to be built — they are things that should already work and do not.
They came out of walking the sign-in flow while answering §8, and they belong in the same pass.

**1. A returning person is matched against a stale address, which is a way into someone else's
account.** A new sign-in method joins an existing account when its verified address matches — but
the address it matches is the one the account was *created* with, and that snapshot is never
updated when the provider later reports a different one. So an address the owner abandoned years
ago still opens their account. Work and education addresses are reassigned to new staff as a matter
of routine, and lapsed domains can simply be bought. The person who receives that old address next
signs in normally, matches, and lands inside the previous owner's account, with their premium,
their purchase history, and the ability to delete it.

The existing defence — refusing sign-ins the provider has not marked as verified — stops someone
*claiming* an address they do not control. It does nothing against someone who genuinely controls an
address that used to belong to somebody else.

The fix needs no new data: match against the addresses the **sign-in methods currently report**,
which are already kept up to date, instead of the account's original snapshot. A stale address then
stops matching by itself.

**2. Joining a new sign-in method to an account is silent.** Nobody is told. Any takeover — by the
route above or another — leaves nothing the owner would notice. One message, *"a new sign-in method
was added to your account"*, is the ordinary safeguard.

**3. Where two accounts share an address, one of them is unreachable.** Resolution takes the
lowest-numbered match, so the other owner is silently shown an account that is not theirs, with no
error raised. Older installations can contain such pairs.

**4. An account can be permanently keyed on an address that stops working.** A private relay address
is a legitimate verified mailbox and is treated as one — correctly — but because the account keeps
the address it was created with, an account can end up keyed forever on a relay the person later
switches off. It then holds their premium and cannot be reached by any message we send. Fix 1 helps
here too, and it is one more reason the code is the real way back (§7).

Only the first is urgent, and its fix is a change to one lookup rather than a redesign.

### Sequencing

**The invoice one has an order of operations.** The privacy policy and the public deletion page are
corrected first, in all thirteen languages; only then does the behaviour change. Shipping the code
first would open a period in which we keep data our own published policy says we destroyed — worse
than either choice on its own. Nothing waits on an outside opinion: the wording names the legal
obligation rather than a number of years (§5), so it can be written today.

## 13. Where the wording lives

Three places must agree, and all three are translated:

- The confirmation the app shows before deleting.
- The **Delete Your Account (Forget Me)** section of the CONNECT privacy policy.
- The public account-deletion page on our website.

Change one, change all three. The English is authored once and every other language is generated
from it.
