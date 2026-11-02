# Approvals and the Feast switch

## Why

The dashboard should only ever say money is in the box when it is. Anything a
member records waits for the cashier to confirm it.

## Who does what

| Role | Records money | Their entries | Approves |
|---|---|---|---|
| Finance | yes | approved on save | yes |
| Admin | yes | approved on save | yes |
| Secretariat | yes | pending | no |
| Executive | payments on existing bills, expenses, bills against an advance | pending | no |
| Viewer | no | | no |

Cash advances (issue, edit, cash returned) are Finance-only.

## What counts as money

Only **approved** money reaches Cash in Hand, Bank, Total Collected, target
progress, Spent, Budget vs actual, reports, year rollover and Asura.

* A payment waiting for approval still shows on the donor's bill, so nobody
  chases them twice. It is "held by" the member who recorded it (cash) or
  "online, awaiting confirmation" (bank), shown on the dashboard's
  *Not in the fund yet* card.
* An expense a member paid from their own pocket counts as spent once
  approved, and the committee owes them until Finance marks it refunded.
* A bill against an advance leaves the advance outstanding until approved.

## Finance's three actions

**Approve** (optionally for less: for a payment the remainder stays with the
member; for an expense the difference is dropped, or stays outstanding on the
advance), **send back** with a note (the member fixes and resubmits), **reject**
with a reason (the bill goes back to unpaid). Everything is recorded in History.

## Feast

`organizations.feast_enabled`. Admin switches it under Settings → Committee.
Off hides the Feast pages, dashboard card and tokens, and refuses Feast writes;
nothing is deleted. New committees start with Feast off.

## Rolling out

Existing rows migrate as `approved`, so no committee's totals change. The
migration also adds `pending_collected` to the three income bill tables and
rewrites `v_budget_vs_actual`, `v_contract_status` and `v_advance_balances` to
read approved expenses only.
