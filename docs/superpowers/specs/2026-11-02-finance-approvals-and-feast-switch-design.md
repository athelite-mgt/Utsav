# Finance approvals and the Feast switch — design

Date: 2026-11-02
Status: agreed with Boss in conversation, awaiting his read of this document

## Why

Last season the dashboard said money was in the cash box when it wasn't:

- A member collected a membership, recorded it, and forgot to hand the notes
  to the cashier. The dashboard counted it as cash in hand; the box was short.
- Members added expenses without telling the cashier, so "spent" and the fund
  moved without the person holding the box knowing.

The fix is that **money only reaches the fund once the Finance Secretary
confirms it**. Until then the app says plainly who is holding what and what
is waiting for a decision.

Success: at any moment, Cash in Hand on the dashboard matches what is
physically in the box, Bank matches the account, and there is one list of
"who is holding how much" and "what is waiting for approval".

Separately, the Feast section is a special feature most committees don't use;
it needs an on/off switch.

## Goals

1. Executive members (and everyone else who isn't Finance) can record money
   events, but those events stay **pending** until a Finance user acts on them.
2. Pending income is shown as **held by the member who recorded it**, not as
   cash in hand or bank.
3. Pending expenses don't count as spent, don't reduce the fund, and don't
   show in budget actuals.
4. Finance has one place to work through everything pending.
5. A per-committee setting turns the Feast section on and off.

## Non-goals

- Approval for Feast money. Feast stays exactly as it is (Boss: "Feast is just
  a special feature").
- Approval for Offerings (they are goods, not money), Budget lines, Vendors,
  Contracts, Fund transfers, Workflow, Schedule.
- Multi-step approval chains, approval thresholds, or more than one approver
  per item.

## Anti-goals (would be a failure even if it "works")

- The fund position ever counting money Finance hasn't confirmed.
- A rupee counted twice or zero times across pending → approved → refunded,
  or across an advance → settlement expense.
- Finance's own entries being slowed down by a queue — the cashier holds the
  box, so their entries are already confirmed.
- Existing data (Bankplot Kali Puja's imported 2025, Bankplot Durga's data)
  changing its totals when this ships.

## Who can do what

| Role | Records income / expenses | Their entries | Approves |
|---|---|---|---|
| Finance | yes | go straight through (approved on save) | yes |
| Admin | yes (as today) | go straight through (approved on save) | yes |
| Secretariat | yes (as today) | **pending** | no |
| Executive | **yes (new)** | **pending** | no |
| Viewer | no | — | no |

Boss: "admin or Finance can approve." This also means a committee with no
Finance user still works (its Admin approves). Since Admin can approve, an
Admin's own entries go straight through like Finance's *(my reading — an
approver queuing items for themselves would be pointless)*. Everyone else
waits for Finance or Admin. Wherever this document says "Finance approves",
read "Finance or Admin".

**Cash Advances** — Boss: "only finance". Issuing an advance and recording
cash returned from one become **Finance-only** actions (Executive, Secretariat
and Admin lose them; no pending state needed because only the cashier can do
them). Members can still file the **settlement expense** for an advance they
hold; that expense goes pending like any other. *(Reading of "only finance":
taken literally as the Finance role, so Admin can't issue advances either.)*

Scope of "money" that goes through approval (Boss: "all except feast"):
Membership, Special Donation, Advertisements, Other Incomes — i.e. the
`subscriptions`, `ad_bookings` and `special_donations` instalments in
`collection_payments` — and Expenses (`expenses`).

What Executive gains is the ability to **record collections against existing
bills and to file expenses**. Creating or editing bills themselves (donor,
billed amount, bill number, ad bookings, budget) stays with Admin /
Secretariat / Finance as today — builder's call on the exact field split, as
long as Executive can't move money without it going pending.
*(My call: bills aren't cash, so they don't need approval; only the cash
events do.)*

## How income behaves

Approval sits on each **instalment** (`collection_payments` row), not on the
bill, because that is where cash actually moves and where the fund position
already reads from.

- A non-Finance user records a payment → instalment saved as **pending**,
  attributed to them (`recorded_by_user_id` / name already exist).
- The donor's bill shows the payment as collected (with a "pending" badge), so
  nobody goes back to chase the donor.
- The fund position does **not** count it in Cash in Hand or Bank. It appears
  under a new **"Held by members"** figure with a per-person breakdown
  (who, how much, oldest item).
- Finance approves → it moves into Cash in Hand or Bank by its payment mode,
  using the existing `PaymentModeClassifier` split.

**Online / bank-mode income** is also pending: Boss agreed approval happens
when Finance has "seen the GPay in the account". It is not shown as cash
*held* by the member, though — it is listed as **"Online, awaiting
confirmation"** (attributed to the recorder), and moves into Bank on approval.
Cash-mode pending is what makes up "Held by members". *(My call, from the
agreed rule plus the success test "Bank matches the account".)*

**Which figures count pending income** *(my call)*:

- *Counts pending* (so the donor isn't chased twice): the bill's own
  collected/due, the Membership/Ads/Special/Other Incomes lists, dues lists.
- *Approved only*: Cash in Hand, Bank, the dashboard's Total Collected,
  target progress, per-stream totals, "Balance in hand", the audit report's
  Receipts & Payments, rollover's `prior_collected`, and Asura's money
  answers. The dashboard shows pending income as its own separate figure
  next to these, so nothing silently disappears.

## How expenses behave

An expense recorded by a non-Finance user says how it was paid:

1. **From the cash box / bank** — straight spending; pending until approved,
   then counts as spent and reduces the fund like today.
2. **From their own pocket** — pending until approved. On approval it counts
   as spent, and the committee **owes that member** the amount. It does not
   leave the fund until Finance records the refund ("Mark refunded", with
   date and mode); the dashboard shows an "Owed to members" figure until then.
3. **From an advance** — the existing Cash Advance settle flow. The settlement
   expense is pending until approved; the advance stays open against the
   member until then, so the rupee is counted exactly once at each step
   (today's fund-position invariant must still hold).

Bank-mode expenses follow the same rule: pending until Finance confirms them.

**Approved for less than claimed** *(my call)*: for an own-pocket claim the
committee owes only the approved amount and the difference is dropped (noted
with Finance's reason); for an advance settlement the difference stays
outstanding on the advance, so the member still owes it back or files another
bill.

Pending expenses are listed (with a badge) but excluded from spent totals,
budget-vs-actual, reports and the fund position. Rolling a year over must use
approved spending only.

## What Finance can do with a pending item

Boss: "can do all of them".

- **Approve**, optionally changing the amount first. For income: if a member
  hands over ₹400 of a recorded ₹500, Finance approves ₹400 and the remaining
  ₹100 stays pending/held by that member until handed over.
- **Send back** with a note → returns to the member who recorded it; they fix
  and resubmit (back to pending).
- **Reject** with a reason → the entry is cancelled. For income the bill goes
  back to unpaid by that amount.

Every action is recorded with who and when, and shows in the existing History
(audit) trail.

Editing: a non-Finance user can edit or withdraw their own pending / sent-back
items. Once approved, only Finance can change the money on it; any change to
an approved item's amount by Finance applies immediately. *(My call.)*

## Where Finance works

A **"Pending approval"** page (and a count badge in the menu / dashboard tile
for Finance) listing every pending and sent-back item for the active year:
type, member who recorded it, donor or expense detail, amount, mode, date, and
the three actions. Group or filter by member, since the real-world flow is
"Rakesh hands over a stack of notes; approve all of his".
*(Bulk-approving a member's items is my call; it matches how handovers happen.)*

Members see their own pending / sent-back items (with Finance's note) on their
dashboard.

**Year rollover** *(my call)*: the Pending approval page shows pending items
from **every** year, not just the active one, so nothing falls out of the
queue. Rolling a year over is allowed but warns how many items, held amounts
and refunds owed are still open in the year being closed.

## Notifications (my call)

Using the existing push notifications:

- Finance users: when a new item is submitted for approval.
- The recording member: when their item is sent back or rejected.

## Existing data

Everything already in the database is treated as **approved** when this ships,
so no committee's totals change (anti-goal above). The imported Bankplot Kali
Puja 2025 data and Bankplot Durga's data must show identical dashboard figures
before and after the migration.

## Feast switch

- Per-committee setting **"Feast"** under Settings → Committee; Admin only.
- **Off** hides: the Feast page and its menu entry, the Feast collection card
  on the dashboard, and the feast token sheet. Feast endpoints for that
  committee should refuse writes while off.
- **On** brings everything back. Turning it off never deletes data.
- **New committees start with Feast off** (Boss).
- Existing committees: the migration leaves them **on** so nothing disappears
  unannounced; Bankplot Kali Puja is then switched **off** (Boss) — via the
  new setting after deploy. *(Keeping Durga on is my call; Boss can flip it.)*

## Constraints

- Multi-tenant rules hold: every new column/table is tenant-scoped with the
  same RLS predicate and EF filter as its neighbours; the isolation test suite
  must cover any new table or endpoint.
- Migrations run on startup in production (`Database:MigrateOnStartup`); they
  must be safe on the live database with data in it.
- `PersistCollectionPaymentsAsync` currently deletes and re-creates every
  instalment of a bill on each save. That must not wipe approval state:
  an approved instalment stays approved unless Finance changes it, and a
  non-Finance save can only add/alter pending instalments.
- The server enforces all of this; the UI hiding buttons is a convenience only
  (same posture as `PermissionMatrix`).
- The repo is public: no committee or member data in code, tests or fixtures.

## Left to the builder

- Schema shape (status columns vs a separate approvals table), naming, and how
  the "held by" / "owed to" figures are computed, provided the fund-position
  invariant (each rupee counted exactly once) holds and is tested.
- Exact field-level split of what Executive may edit on a bill.
- Page layout of the Pending approval screen and badges.
- Whether Asura (AI assistant) answers include pending items (they must at
  least never present pending money as in the fund).
