# Finance approvals, Feast switch and Puja data migration — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans (inline). Steps use checkbox syntax.

**Goal:** Money recorded by anyone but Finance/Admin stays pending until approved; the Feast section has an on/off switch; the old single-committee Puja database is migrated into the multitenant database.

**Architecture:** Add a `status` to the two things that move money — `collection_payments` (income instalments) and `expenses` — plus decision/claim columns. Every query that feeds the fund, budget, reports and rollover filters to `status='approved'`. Approval decisions are made by one new endpoint group that writes through `DynamicSql.ExecuteAsync` so History audits them. Feast on/off is one `organizations.feast_enabled` column read by a guard and the UI.

**Tech Stack:** .NET 10 minimal APIs, EF Core 10 + SQL Server RLS, xUnit (unit + isolation on the dev SQL container), React/TypeScript + vitest.

**Spec:** `docs/superpowers/specs/2026-11-02-finance-approvals-and-feast-switch-design.md`

## Global Constraints

- Approvers are **Finance and Admin**. Their own entries are approved on save. Secretariat and Executive entries are `pending`.
- Statuses (exact strings): `approved`, `pending`, `sent_back`, `rejected`. Existing rows migrate to `approved`.
- Approval covers `subscriptions`, `ad_bookings`, `special_donations` instalments (`collection_payments`, `parent_type <> 'feast'`) and `expenses`. Not Feast, offerings, budget, vendors, transfers.
- Advances: issuing, editing and recording a `return` become **Finance-only** (new permission `ManageAdvances`). Members still file the settlement *expense*, which goes pending.
- Pending income is shown on the bill but **excluded** from Cash in Hand, Bank, Total Collected, target progress, per-stream totals, Balance in hand, audit Receipts & Payments, rollover `prior_collected`, Asura money answers.
- Pending cash-mode instalments are "Held by members" (per `recorded_by_name`); pending bank-mode are "Online, awaiting confirmation".
- Own-pocket expenses (`paid_from = 'own_pocket'`): on approval the committee owes the member until `refunded_on` is set.
- Feast: `organizations.feast_enabled`, default **on** for existing committees, **off** for committees provisioned after this ships. Admin-only toggle. Bankplot Kali Puja is switched off after deploy.
- Public repo: no member names or committee data in code, tests or fixtures. Tests use invented names.
- Server enforces everything; UI hiding is convenience.

## Review Focus

- A non-Finance `PATCH` that re-sends an already-approved instalment list must not wipe or re-pend it (test in Task 4).
- An Executive trying to create/edit a bill, change `billed`, or touch a table outside their allowance gets 403 (Task 4).
- Approving for less than claimed: income remainder stays pending; own-pocket difference dropped; advance-settlement difference stays outstanding (Tasks 4, 5).
- Reject on income restores the bill's due by exactly that amount and never goes negative (Task 7).
- Existing committees' dashboard figures are identical before and after the migration (Task 1 test over seeded rows).
- Feast off: Feast endpoints refuse writes and the dashboard payload omits the card (Task 9).

---

### Task 1: Schema — status columns, feast flag, approval-aware views
**Files:** Create migration `AddApprovals` in `src/Utsav.Infrastructure/Data/Migrations/`; modify entities `CollectionPayment`, `Expense`, `Organization`, `UtsavDbContext.cs`, `DatabaseViews.cs`; Test `tests/Utsav.IsolationTests/ApprovalSchemaTests.cs`.
**Interfaces — Produces:** columns `status nvarchar(20) NOT NULL DEFAULT 'approved'`, `decided_by_user_id uniqueidentifier NULL`, `decided_by_name nvarchar(200) NULL`, `decided_at datetime2 NULL`, `decision_note nvarchar(500) NULL` on `collection_payments` and `expenses`; `expenses` also `paid_from nvarchar(20) NOT NULL DEFAULT 'fund'`, `recorded_by_user_id`, `recorded_by_name`, `refunded_on nvarchar(10) NULL`, `refund_mode nvarchar(50) NULL`; `organizations.feast_enabled bit NOT NULL DEFAULT 1`; `DatabaseViews.ApprovalAwareBudgetVsActual / ApprovalAwareContractStatus / ApprovalAwareAdvanceBalances` (ALTER VIEW text).
- [ ] Write `ApprovalSchemaTests`: seed a pending and an approved expense; assert `v_budget_vs_actual.actual` counts only the approved one; a pending settlement expense leaves `v_advance_balances.outstanding` unchanged; a `return` settlement always counts. Run → FAIL (no column).
- [ ] Add entity properties, EF mapping, `dotnet ef migrations add AddApprovals`; migration `Up` also runs the three ALTER VIEW statements (derived from the original constants by replacing `FROM expenses` with `FROM (SELECT * FROM expenses WHERE status = 'approved') expenses`; advance view written out so only `return` or approved-expense settlements count). A unit test asserts each replacement matched at least once.
- [ ] Run isolation + unit suites → PASS. Commit.

### Task 2: Permissions and the approval policy
**Files:** Modify `Permission.cs`, `PermissionMatrix.cs`; create `src/Utsav.Domain/Authorization/ApprovalPolicy.cs`; Test `tests/Utsav.UnitTests/Authorization/ApprovalPolicyTests.cs`.
**Produces:** `Permission.ApproveMoney` (Finance, Admin), `Permission.SubmitMoney` (Executive, Secretariat), `Permission.ManageAdvances` (Finance). `ApprovalPolicy.StatusFor(AppRole) -> string` (`approved` for Finance/Admin else `pending`); `ApprovalPolicy.CanApprove(AppRole) -> bool`. Advances: `WriteAdvances` is replaced by `ManageAdvances` for issue/edit/return only; Executive keeps read.
- [ ] Tests: matrix rows for all five roles × the three new permissions; `StatusFor` for every role. FAIL → implement → PASS → update the table in the `PermissionMatrix` doc comment. Commit.

### Task 3: Feast switch (server)
**Files:** `BrandingEndpoints.cs` or a new `FeastSettingEndpoints.cs`; `ModuleEndpoints.cs` feast routes; `TenantProvisioner.cs`; `CrudEndpoints.cs`; Test `tests/Utsav.IsolationTests/FeastSwitchTests.cs`.
**Produces:** `PATCH /api/organizations/current/feast {enabled:bool}` (ManageSettings); `GET /api/organizations/current` includes `feast_enabled`; Feast read routes return 404 and Feast table writes (`feasts`, `feast_expenses`, `feast_tokens`) return 403 `{error:"Feast is switched off for this committee"}` while off; the provisioner sets `feast_enabled = false` for new committees.
- [ ] Tests: default on for a seeded org; Admin can switch off, Finance cannot (403); while off, POST /api/feasts → 403 and GET /api/feasts → 404; data survives off→on; a newly provisioned org starts off. FAIL → implement → PASS. Commit.

### Task 4: Income pending path (CRUD)
**Files:** `CrudEndpoints.cs`, `TableRegistry.cs`; Test `tests/Utsav.IsolationTests/IncomeApprovalTests.cs`, `tests/Utsav.UnitTests/CrudEndpointsTests.cs`.
**Behaviour:** an Executive may `PATCH` subscriptions/ad_bookings/special_donations only with columns `collected, mode, collected_on, comments, updated_at, payments`; never POST/DELETE; 403 otherwise. `PersistCollectionPaymentsAsync` gains the caller's role: approvers behave as today (new instalments `approved`); non-approvers keep every existing approved instalment untouched, add new ones as `pending` (recorded_by set), may remove only their own `pending`/`sent_back` ones, then the parent's `collected` is recomputed as the sum of non-rejected instalments. Notification hook is added in Task 8.
- [ ] Tests (invented names): Executive adds ₹500 cash → one `pending` row, bill collected 500; Finance's same call → `approved`; Executive PATCH re-sending an approved instalment plus a new one keeps the approved one approved; Executive removing an approved instalment is ignored; Executive POST bill → 403; Executive changing `billed` is dropped. FAIL → implement → PASS. Commit.

### Task 5: Expense pending path and advances
**Files:** `CrudEndpoints.cs` (expenses), `AdvanceEndpoints.cs`, `TableRegistry.cs`; Tests `ExpenseApprovalTests.cs`.
**Behaviour:** `expenses` writable by `SubmitMoney` holders (create; edit/delete only own non-approved rows). Columns `paid_from` joins the whitelist; `status`, `recorded_by_*`, `decided_*`, `refunded_on` are server-set, never accepted from the body. Non-approver rows are `pending`; approver rows `approved`. `AdvanceEndpoints`: create/update/return require `ManageAdvances`; `settle` kind `expense` stays open to `SubmitMoney` roles and inserts the expense with the caller's status.
- [ ] Tests: Executive expense → `pending`, absent from `/api/fund_position` spent; Finance expense → `approved`; Executive cannot set `status` via body; Executive issuing an advance → 403, Finance → 200; Executive settling with a bill → expense `pending`, advance `outstanding` unchanged. FAIL → implement → PASS. Commit.

### Task 6: Fund position — approved only, held and owed
**Files:** `FundPositionEndpoint.cs`; Test `FundPositionApprovalTests.cs`.
**Produces:** `status='approved'` on the `in`/`out` rows; response gains `pending: { heldByMembers: [{name, amount, oldest}], heldTotal, onlinePending, pendingExpenses, owedToMembers: [{name, amount}], owedTotal }`. Own-pocket approved unrefunded expenses count as spent (budget) but are subtracted from cash/bank only when refunded (a refund moves `amount` out of the side named by `refund_mode`). `BreakdownAsync` also approved only.
- [ ] Tests cover the exactly-once invariant across: pending → approved (cash +X once), own-pocket approved (spent +X, fund unchanged) → refunded (fund −X once), advance → pending settlement → approved. FAIL → implement → PASS. Commit.

### Task 7: Approvals endpoints
**Files:** create `src/Utsav.Api/Endpoints/ApprovalEndpoints.cs`, map in `Program.cs`; Test `ApprovalFlowTests.cs`.
**Produces (all `ApproveMoney` except the two reads):** `GET /api/approvals` (every year; items `{kind: 'income'|'expense', id, year_id, status, recorded_by_name, label, amount, mode, date, note}`) and `GET /api/my/approvals` (caller's own pending/sent-back); `POST /api/approvals/{kind}/{id}/approve {amount?, note?}`, `/send_back {note}` (note required), `/reject {reason}` (reason required), `POST /api/approvals/approve_member {name}` (bulk), `POST /api/expenses/{id}/refund {refunded_on, mode}`. Approve-with-lower-amount: income splits into an `approved` row for the amount and a `pending` row for the remainder; expense own-pocket drops the difference; an advance-settlement expense also lowers its settlement amount and re-evaluates the advance's auto-close. Reject recomputes parent `collected`. Writes go through `DynamicSql.ExecuteAsync` so History records them; every action sets `decided_by_*`, `decided_at`.
- [ ] Tests: approve full; approve ₹400 of ₹500 leaves a pending ₹100; send back without note → 400; reject restores due; bulk approve by name; non-approver → 403; cross-tenant id → 404. FAIL → implement → PASS. Commit.

### Task 8: Reports, rollover, Asura, notifications
**Files:** `AuditReportEndpoint.cs`, `AdminEndpoints.cs` (rollover), `Asura/*`, `NotifiableTables.cs`/push sender; Tests alongside.
- [ ] Receipts & Payments, dues, Total Collected sources, rollover `prior_collected`/`prior_billed` and Asura money queries filter approved; subscription/ad/special list queries add `pending_collected`. Push: on a pending submission notify Finance+Admin members; on send-back/reject notify the recorder. Tests: report totals ignore a pending row; rollover ignores it; a notification row is queued for each case. Commit.

### Task 9: Frontend
**Files:** `src/Utsav.Web/src/…` (api types in `types/db.ts`, `Dashboard.tsx`, expense and collection dialogs, a new `pages/Approvals.tsx`, menu, `Settings`), vitest.
- [ ] Types for `status`, `pending`, `feast_enabled`; Pending approval page (filter/group by member, approve/amount/send back/reject, "Approve all of <member>"); badges on bills and expenses; "Paid from: Committee / My own pocket" on the expense dialog; dashboard tiles *Held by members*, *Online awaiting confirmation*, *Owed to members*, *Awaiting approval*; Total Collected / target progress use `collected − pending_collected`; my pending items card; Feast menu/page/card/token-sheet hidden when off; Settings → Committee switch (Admin); Executive sees the record-payment and add-expense actions. vitest for the pure helpers (approved-collected math, status badge text, feast visibility). `npm run build` and `npm test` green. Commit.

### Task 10: Full verification and local UI check
- [ ] `dotnet test` (unit + isolation) and `npm test` green; run the local stack and click through: Executive records a payment and an expense, Finance sees them pending, approves one with a lower amount, rejects one, sends one back; dashboard figures move once; Feast off hides everything. Fix what breaks.

### Task 11: Puja database → Durga committee migration
**Files (scratchpad, not in repo):** `gen_puja_migration.py`/SQL; runs against `MultiTenatPuja` only; reads from `Puja` read-only.
- [ ] Inspect the old `Puja` schema and row counts (read-only). Extract each table, remap into org `bpdurga` (new ids kept where possible, `org_id` stamped, `status='approved'`), refusing to run if the committee already has data. Transactional, with a final count/total comparison against the old database. Run in the Azure Query editor as for the Kali import; verify in the app. Old database is left untouched (Boss turns it off).

### Task 12: Ship
- [ ] Commit, push, watch the pipeline (migrations auto-apply), run the AddApprovals migration check on prod, switch Bankplot Kali Puja Feast off, spot-check both committees in production. Update `docs/DEPLOYMENT.md` and write the summary.
