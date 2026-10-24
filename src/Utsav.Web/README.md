# 🪔 Puja Finance — Durga Puja Committee Manager

A mobile-friendly web app to run your Durga Puja committee's finances end-to-end:
household **Chanda** collection, **advertisements**, **special donations**, in-kind
**offerings**, **expenses**, **budget vs actual**, a **cash ledger**, and a live
**dashboard** — all seeded from your `DurgaPuja_Finance-2025-final.xlsx` workbook.

**Runs entirely on your machine.** No cloud account, no Docker, no signup.

---

## 🚀 Quick start (2 commands)

```bash
npm install
```
```bash
npm run seed && npm run dev
```

Open **http://localhost:5173** → click **Create account** → you're in.
The **first account created becomes the Admin**.

That's it. `npm run seed` builds a local SQLite database from your Excel workbook;
`npm run dev` starts the web app and the API together.

---

## 🧰 Stack

100% free, fully local, zero native builds:

| Layer | Choice |
|---|---|
| Front end | React + Vite + TypeScript + Tailwind (shadcn-style components) |
| Data / charts | TanStack Query · Recharts (colorblind-safe palette) |
| API | Express 5 |
| Database | **SQLite** via Node's built-in `node:sqlite` — a single file at `data/puja.db` |
| Auth | scrypt password hashing + session tokens (`node:crypto`) |

The web app runs on **5173**, the API on **5174** (Vite proxies `/api` across).

---

## ✨ Features

| Module | Highlights |
|---|---|
| **Dashboard** | Collection vs target, spend, balance, dues; income-by-stream donut, top spending, budget-vs-actual |
| **Chanda** | 183-donor register, multi-year history, projected ask, bill/collected/due, collector & mode, search + filters + CSV |
| **Advertisements** | Souvenir sponsors with ad type, workflow status, and last year's amount for context |
| **Special Donations** | One-off / major gifts with bill tracking |
| **Offerings** | In-kind pledges (saree, dhuti, vog, flowers) per puja day, one-tap "mark received" |
| **Expenses** | Categorised spending across 17 categories |
| **Budget** | Forecast vs **actual** — actual is computed live from expenses by a SQL view |
| **Cash Ledger** | Petty cash / who's-holding-money, with net position |
| **Settings** | Puja year & target, collectors, zones, editable dropdown lists, user roles |

### Roles
- **Admin** — everything. The first account to sign up is automatically Admin.
- **Collector** — can record income (Chanda, ads, special donations, offerings) but
  *cannot* touch expenses, budget, cash ledger, or settings.

Enforced server-side in the API, so it holds even if someone calls the endpoints
directly. Promote/demote people under **Settings → Users & Roles**.

---

## 📜 Commands

| Command | What it does |
|---|---|
| `npm run dev` | Web app + API together (this is the one you want) |
| `npm run seed` | (Re)build `data/puja.db` from the Excel workbook — safe to re-run |
| `npm run db:reset` | Delete the database and re-seed from scratch |
| `npm run import:excel` | Preview what the parser reads, without touching the DB |
| `npm run build` | Production bundle into `dist/` |
| `npm start` | Serve the built app + API from one process on port 5174 |

> Your data lives in **`data/puja.db`**. Back it up by copying that one file.
> It's git-ignored because it contains real donor names and amounts.

---

## 🧱 Project structure

```
├─ DurgaPuja_Finance-2025-final.xlsx   your source workbook
├─ data/puja.db                        the local SQLite database (git-ignored)
├─ server/
│  ├─ schema.sql                       tables, generated columns, budget view
│  ├─ db.mjs                           connection, migrations, reference data
│  ├─ auth.mjs                         scrypt hashing, sessions, role middleware
│  └─ index.mjs                        Express API
├─ scripts/
│  ├─ parse-excel.mjs                  workbook → normalized records
│  └─ seed.mjs                         records → SQLite
├─ src/
│  ├─ pages/                           one file per module
│  ├─ components/ui|charts|layout|form
│  ├─ hooks/  context/  lib/           queries, auth, active year, formatting
└─ supabase/migrations/                Postgres equivalents, kept for a future cloud move
```

## 🗄️ Where the data comes from

- **donors + subscriptions** ← *Chanda* sheet · **advertisers + ad_bookings** ← *Add*
- **special_donations** ← *Spcl_Donation* · **offerings** ← *Offerings*
- **expenses** ← *Expense* · **budget_lines** ← *Budget* (actual computed live)
- **cash_ledger** ← *Sheet1* · **zones / collectors / dropdowns** ← *MetaData*

Two rules were reverse-engineered from the sheet and are encoded in the app:
- **Projected ask = last year's bill × 1.065**
- **due = billed − collected**, a generated column the database maintains itself

The import reproduces your workbook's own Dashboard exactly — collected **₹1,27,433**,
spent **₹1,23,246**, in hand **₹4,187**.

## 🌐 Moving to the cloud later

The SQL in `supabase/migrations/` is the Postgres/Supabase equivalent of
`server/schema.sql`, including Row Level Security policies matching the same role
model. When you're ready to host it, create a Supabase project, run those migrations,
and point the front end at it — the UI and data shapes stay the same.

---
*Made for the committee — Subho Sharodiya! 🌸*
