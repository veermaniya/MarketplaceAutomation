# Multi-Marketplace Automation

ASP.NET MVC (.NET 8) system for managing a single product catalogue and pushing it to **Flipkart, Amazon, and Meesho** via Playwright + Hangfire background jobs. Built from the roadmap doc — scope: full foundation, credential slots ready for you to fill in.

---

## Solution layout

```
MarketplaceAutomation/
├── database/
│   ├── 01_Schema.sql               9 tables + indexes + seed roles
│   └── 02_StoredProcedures.sql     Duplicate validation + retry dequeue SPs
├── src/
│   ├── MA.Core/                    Entities, enums, interfaces, DTOs
│   ├── MA.Data/                    EF Core DbContext, repositories, services
│   ├── MA.Automation/              Playwright drivers + factory
│   ├── MA.Jobs/                    Hangfire job definitions
│   └── MA.Web/                     ASP.NET MVC frontend (DI host)
└── MarketplaceAutomation.sln
```

---

## Setup (one-time)

### 1. SQL Server

Run the two scripts against any SQL Server (LocalDB works fine for dev):

```bash
sqlcmd -S "(localdb)\MSSQLLocalDB" -i database/01_Schema.sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -i database/02_StoredProcedures.sql
```

This creates the `MarketplaceAutomation` database, all 9 tables from the roadmap, and seeds Admin/Manager/Operator roles.

Update the connection string in `src/MA.Web/appsettings.json` → `ConnectionStrings:DefaultConnection` if you're not using LocalDB.

> **EF migrations** are optional — the schema SQL is the source of truth. If you prefer EF, run `dotnet ef migrations add Init -p src/MA.Data -s src/MA.Web` instead of step 1.

### 2. JWT signing key

Replace the placeholder in `appsettings.json`:

```json
"Jwt": {
  "Key": "REPLACE-WITH-A-256-BIT-RANDOM-STRING-AT-LEAST-32-CHARS-LONG",
  ...
}
```

Use any 32+ character random string. For production, store via `dotnet user-secrets` or environment variables, not in source.

### 3. Build & restore

```bash
dotnet restore
dotnet build
```

### 4. Install Playwright browsers (first time only)

After the first build:

```bash
pwsh src/MA.Web/bin/Debug/net8.0/playwright.ps1 install chromium
```

On Linux/macOS without PowerShell: `dotnet src/MA.Web/bin/Debug/net8.0/Microsoft.Playwright.dll install chromium`.

### 5. Run

```bash
cd src/MA.Web
dotnet run
```

Open `https://localhost:5001`. The **first user you register becomes Admin**; subsequent users are Managers.

---

## Adding marketplace credentials

This is the "fill in later" part. Credentials are **never** stored in `appsettings.json` — they go in the `MarketplaceAccounts` table, encrypted via ASP.NET Data Protection.

1. Log in.
2. Go to **Accounts** → **+ Add Account**.
3. Choose marketplace, enter username/password (and optional API key/secret/seller id).
4. Submit. The plaintext is encrypted before save.

**Important:** the Data Protection keys live in `src/MA.Web/App_Data/keys/`. Back this folder up alongside your database. If keys are lost, stored credentials become undecryptable and accounts will need to be re-entered.

For Amazon SP-API specifically, fill in `Marketplaces:Amazon:LwaClientId`, `LwaClientSecret`, and `RoleArn` in `appsettings.json` (these are app-level, not per-account).

---

## What's working end-to-end

- ✅ User registration/login (cookie auth for MVC, JWT endpoint at `POST /Account/Token` for API)
- ✅ Role-based authorisation (Admin / Manager / Operator)
- ✅ Product Master CRUD with duplicate detection (Section 5 logic enforced in 3 places: app, DB unique indexes, and `sp_Product_ValidateDuplicates`)
- ✅ Bulk CSV upload via CsvHelper
- ✅ Marketplace account management with encrypted credentials
- ✅ Mapping creation that queues Hangfire jobs
- ✅ Hangfire dashboard at `/hangfire` (Admin only)
- ✅ Retry queue with exponential backoff (1, 2, 4, 8, 16 min, max 5 attempts → Dead)
- ✅ Automation logs with per-action duration + status
- ✅ Playwright base driver with per-account persistent browser profile

## What's stubbed (deliberate — see `// TODO:` comments)

- ⚠ `FlipkartAutomation.CreateListingAsync` — needs category mapping + form fill
- ⚠ `FlipkartAutomation.PushInventoryAsync` / `FetchOrdersAsync`
- ⚠ `AmazonAutomation.*` — recommended path is SP-API (`JSON_LISTINGS_FEED`, `PATCH /listings/...`, `GET /orders/v0/...`) rather than browser automation
- ⚠ `MeeshoAutomation.*` — same pattern as Flipkart

Login flows are implemented for all three with OTP/MFA handling (waits up to 2 min for manual entry the first time, then re-uses persistent context).

---

## Architecture notes

- **Cookie + JWT dual auth**: Cookie for browser-facing MVC views, JWT for programmatic API consumers. Cookie is the default scheme.
- **Credential encryption**: `ICredentialProtector` is the contract; `DataProtectionCredentialProtector` is the implementation. Purpose string is `MA.MarketplaceCredentials.v1` — changing it invalidates existing ciphertexts.
- **Playwright per-account profiles**: `LaunchPersistentContextAsync` at `%APPDATA%\MAAutomation\<marketplace>\<accountId>` preserves cookies, so post-OTP sessions survive across job runs.
- **Headless = false** in drivers because seller portals fingerprint headless browsers heavily. Flip to true once you've stabilised selectors and have a residential proxy.
- **First-registered user = Admin**: simple bootstrap pattern. After that, all new users are Managers; promote via SQL if needed.
- **Hangfire**: SQL Server storage on the same DB. 4 workers by default. Retry worker (`ProcessRetryQueueAsync`) runs every minute via `RecurringJob`.
- **Duplicate prevention** (Section 5) is enforced 3 ways for defence in depth:
  1. App: `sp_Product_ValidateDuplicates` is called before save and returns conflict rows for friendly error messages.
  2. DB: filtered unique indexes on `Products.SKU`, `Products.Barcode`, `MarketplaceMappings(Marketplace, ExternalListingId)`.
  3. UI: form-level validation surfacing the SP results.

---

## Project dependencies

```
MA.Web ──► MA.Core, MA.Data, MA.Automation, MA.Jobs
MA.Jobs ──► MA.Core, MA.Data, MA.Automation
MA.Automation ──► MA.Core
MA.Data ──► MA.Core
MA.Core ──► (none)
```

---

## Common tasks

| Task | How |
|---|---|
| Promote a user to Admin | `INSERT INTO UserRoles (UserId, RoleId) SELECT @uid, RoleId FROM Roles WHERE RoleName='Admin'` |
| View pending retries | Dashboard widget, or `SELECT * FROM RetryQueue WHERE Status='Pending'` |
| See last 50 automation logs | `SELECT TOP 50 * FROM AutomationLogs ORDER BY OccurredOn DESC` |
| Test a driver in isolation | Inject `IMarketplaceAutomationFactory` into a controller/scratch endpoint, call `.Get(Marketplace.Flipkart).LoginAsync(account)` |
| Reset Data Protection (forces re-entry of all credentials) | Delete `src/MA.Web/App_Data/keys/*.xml` and restart |

---

## Next steps when you fill in the drivers

1. **Selectors break**: When a portal updates, run `npx playwright codegen seller.flipkart.com` to record fresh selectors, then update the marked `[VERIFY]` lines.
2. **Amazon**: skip the browser entirely — generate an SP-API LWA refresh token, store it in `MarketplaceAccounts.EncryptedApiKey`, and rewrite `AmazonAutomation` to use `HttpClient` against SP-API endpoints. The factory wiring stays the same.
3. **Order sync schedule**: register a `RecurringJob.AddOrUpdate<MarketplaceJobs>($"fetch-orders-{accountId}", j => j.FetchOrdersAsync(accountId), "*/15 * * * *")` per active account when the account is saved.
