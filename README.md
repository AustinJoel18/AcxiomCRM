# AcxiomCRM

A role-based CRM built with **ASP.NET Core 10 MVC**, **ASP.NET Core Identity**, **Entity Framework Core** (SQLite), **Bootstrap 5** and **Chart.js**. It covers the full customer-sales lifecycle from lead capture through follow-ups and opportunity management, with authentication, authorization, auditing, validation, REST APIs and reporting.

---

## Quick start

Prerequisite: [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
cd AcxiomCRM
dotnet run --project src/AcxiomCRM.Web --launch-profile http
# open http://localhost:5080
```

On first start the app applies EF Core migrations, creates the three roles and, **in Development only**, loads demo data. The SQLite file is created at `src/AcxiomCRM.Web/App_Data/acxiomcrm.db`.

| Demo account (Development) | Role | Scope |
|---|---|---|
| `admin@acxiomcrm.local` | Admin | Everything, plus users/roles/audit |
| `manager@acxiomcrm.local` | Manager | Own + team (Vikram Singh, Sneha Patel) |
| `manager2@acxiomcrm.local` | Manager | Own + team (Arjun Nair, Kavya Reddy) |
| `sales@acxiomcrm.local` … `sales4@acxiomcrm.local` | Sales Executive | Own/assigned records |

The demo password is `Seed:DemoPassword` in `appsettings.Development.json`. It is a development-only value, so change it locally. Self-registration (`/Account/Register`) creates Sales Executive accounts.

- API docs (Development): **http://localhost:5080/swagger** (OpenAPI JSON at `/openapi/v1.json`), plus [`docs/API.md`](docs/API.md)
- Tests: `dotnet test` (87 unit + integration tests against the real app and a throw-away database)

---

## Architecture

The app is a single deployable MVC project with layered folders, plus a test project.

```
src/AcxiomCRM.Web
├── Controllers/            Presentation – MVC controllers (anti-forgery, policies)
│   └── Api/                REST controllers (DTOs only, ProblemDetails errors)
├── Views/ wwwroot/         Razor views, Bootstrap UI, Chart.js, client validation
├── ViewModels/             Form/page models (inherit the API request DTOs → one set of rules)
├── Dtos/                   Application layer contracts (requests, responses, queries)
├── Services/               Application/business services (CRUD, workflows, reports, audit)
├── Domain/                 Business rules: lead workflow, opportunity & follow-up rules
├── Validation/             Custom validation attributes (+ unobtrusive client adapters)
├── Security/               Roles/policies, data-scope (ownership) authorization, JWT, login,
│                           password history, security headers, API CSRF filter
├── Data/                   DbContext (soft delete, auto-audit, append-only guard), seeding, migrations
├── Models/                 Entities and enums
└── Infrastructure/         Options, business clock (IST), formatting, tag helpers
tests/AcxiomCRM.Tests       xUnit: rule/validation unit tests + WebApplicationFactory integration tests
```

Request flow: **View/API → Controller → Service (validates, authorizes scope, applies rules) → DbContext → SQLite**. Security, logging, validation and auditing cut across every layer.

---

## Requirement traceability

| Spec section | Where it is implemented |
|---|---|
| 4.1 Application shell, role-based menu, global search, notifications | `Views/Shared/_Layout.cshtml`, `SearchController`, `ViewComponents/NotificationsViewComponent.cs` (overdue/due-today follow-up reminders) |
| 4.2 / 6 / 17.4 Authentication (Identity, hashing, policy, lockout) | `Program.cs` (Identity options from `Security:*`), `Security/LoginService.cs`, `AccountController` |
| 6.2 Password policy, no reuse of recent passwords | Identity password options + `Security/IdentityExtensions.cs` (`PasswordHistoryValidator`) |
| 6.3 Lockout + admin unlock, audited | Identity lockout (5 attempts / 15 min, configurable), `UserAdminService.UnlockAsync` |
| 7 / 17.10 Role-based authorization | `Security/AppRoles.cs` (policies), `[Authorize(Policy=…)]`, fallback policy = authenticated |
| 7.1 Own / Team / Full data scope | `Security/DataScopeService.cs`, used by every service query and record access |
| 4.3 / 17.11 / 17.12 Dashboard KPIs, date filters and Chart.js charts | `DashboardService`, `Views/Home/Index.cshtml`, `wwwroot/js/charts.js` |
| 4.4 / 17.5 Customers (unique email/phone, duplicate check, history) | `CustomerService`, `CustomersController`, filtered unique indexes |
| 4.5 / 17.6 Leads, status workflow, conversion | `LeadService`, `Domain/BusinessRules.cs` (`LeadWorkflow`), `Views/Leads/Convert.cshtml` |
| 4.6 Follow-ups (complete / missed / reschedule / cancel, overdue) | `FollowUpService`, `FollowUpsController` |
| Activity management (Call / Meeting / Email / Task) | `ActivityService`, `ActivitiesController` |
| 4.7 User & role management | `UserAdminService`, `UsersController`, `RolesController` (permission matrix) |
| 4.8 Opportunities, weighted pipeline, stage board | `OpportunityService`, `OpportunityRules`, `Views/Opportunities/Pipeline.cshtml` |
| 4.9 / 17.16 Audit log (append-only) | `ApplicationDbContext` auto-audit with old/new values; `AuditService` for security events; DB triggers in migration `AuditLogAppendOnly` |
| 4.10 / 10 / 17.14 REST API | `Controllers/Api/ApiControllers.cs`, `docs/API.md`, Swagger UI |
| 4.11 / 11 Reports (filters, sort, paging, CSV) | `ReportService`, `ReportsController`, `Views/Reports/*` |
| 5 / 17.7 / 17.8 Client + server + business validation | `Validation/CrmValidationAttributes.cs`, `wwwroot/js/crm-validation.js`, `RequestValidator` (service boundary), `Domain/BusinessRules.cs`, DB check constraints |
| 12 UI/UX standards | Responsive Bootstrap layout, inline validation, confirm dialogs (`site.js`), accessible labels, friendly error pages |
| 14 Security checklist | See **Security** below |

### Validation rules (exact messages from §5.4)

| Rule | Client | Server attribute | Business layer |
|---|---|---|---|
| Opportunity Amount > 0 (active) / ≥ 0 | ✔ | `[PositiveWhenActive]`, `[Range]` | `OpportunityRules.Validate` |
| Probability 0–100 | ✔ | `[Range(0,100)]` | ✔ + DB check constraint |
| Expected Close Date not in the past (active) | ✔ | `[NotBeforeToday(DependentProperty="Stage")]` | ✔ |
| Follow-up date not before today (new / reschedule) | ✔ | `[NotBeforeToday]` | `FollowUpRules.ValidateDate` |
| Email format | ✔ | `[CrmEmail]` | uniqueness → 409 |
| Phone format (configurable, default 10-digit Indian mobile) | ✔ | `[CrmPhone]` (`Validation:PhonePattern`) | uniqueness → 409 |
| Required / length / numeric range / enum set | ✔ | `[Required]`, `[StringLength]`, `[Range]`, `[ValidEnum]` | DB column limits |

"Today" uses the configured business time zone (`App:TimeZoneId`, default `Asia/Kolkata`).

---

## Security

- **Passwords:** stored only as ASP.NET Core Identity PBKDF2 hashes. The policy covers length, character classes and unique characters, and the last 3 passwords cannot be reused. Admin resets force a change at the next sign-in.
- **Lockout:** 5 failed attempts lock the account for 15 minutes. Every attempt is audited (`LoginFailed`, `Lockout`, `Unlock`). Login endpoints are also rate-limited per IP.
- **Sessions:** HttpOnly, SameSite=Lax cookies that are Secure in production, with sliding expiry. The security stamp is re-validated every minute, so deactivation or a role change ends sessions quickly.
- **API auth:** JWT bearer tokens (HS256, issuer/audience/lifetime validated). Each request re-checks that the user is active, not locked and has an unchanged security stamp. Logout revokes the token ID. Cookie-authenticated, state-changing API calls must send `X-CSRF-TOKEN`.
- **Authorization:** fallback policy (every page requires sign-in), role policies, and resource-level data scope in the service layer. Out-of-scope records return 403 and missing ones 404. Tampered assignee IDs are rejected.
- **Anti-forgery:** global `AutoValidateAntiforgeryToken`, plus explicit `[ValidateAntiForgeryToken]` on every MVC POST.
- **Injection & XSS:** only EF Core LINQ/parameterised queries. Razor encodes all output. A strict CSP (`script-src 'self'`, no inline scripts) plus `X-Frame-Options`, `nosniff` and a referrer policy. CSV exports neutralise spreadsheet formulas.
- **Errors:** users never see stack traces. MVC shows friendly pages and the API returns RFC 7807 ProblemDetails.
- **Secrets:** nothing secret is committed. Supply `Jwt__Key` (32+ characters) and `Seed__AdminPassword` through environment variables or `dotnet user-secrets`. In Development, a temporary JWT key is generated when none is configured.
- **Audit:** append-only, enforced both by the DbContext and by SQLite triggers. Entries record user, timestamp, module, action, entity, record ID, result, old/new values (JSON) and IP. Credentials are never logged.

---

## Configuration

| Key | Default | Purpose |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | `Data Source=App_Data/acxiomcrm.db` | SQLite database |
| `App:TimeZoneId` | `Asia/Kolkata` | Business "today" for date rules |
| `App:EnableApiDocs` | `false` (Development: `true`) | Swagger UI + OpenAPI document |
| `Security:Password:*` | length 8, upper/lower/digit/symbol | Password policy |
| `Security:Lockout:MaxFailedAccessAttempts` / `LockoutMinutes` | 5 / 15 | Lockout policy |
| `Security:PasswordHistoryCount` | 3 | Blocked recent passwords |
| `Security:SessionTimeoutMinutes` | 30 | Cookie lifetime (sliding) |
| `Security:LoginRateLimitPerMinute` | 10 | Per-IP login throttling |
| `Security:AllowSelfRegistration` | `true` | Public registration page |
| `Security:ManagerCanViewAudit` | `true` | Managers get a limited (team, business-only) audit view |
| `Jwt:Key` | *(required outside Development)* | Token signing key |
| `Jwt:AccessTokenMinutes` | 60 | Token lifetime |
| `Validation:PhonePattern` | `^[6-9]\d{9}$` | Phone format (client + server) |
| `Seed:AdminEmail` / `Seed:AdminPassword` | `admin@acxiomcrm.local` / — | Bootstraps the first Admin if none exists |
| `Seed:DemoData` / `Seed:DemoPassword` | `false` (Development: `true`) | Demo dataset |

### Production deployment

```bash
export ASPNETCORE_ENVIRONMENT=Production
export Jwt__Key="$(openssl rand -base64 48)"
export Seed__AdminPassword='<strong initial admin password>'
dotnet publish src/AcxiomCRM.Web -c Release -o out && dotnet out/AcxiomCRM.Web.dll
```

Production turns on HTTPS redirection, HSTS and Secure-only cookies. Put the app behind TLS. Persist ASP.NET Core Data Protection keys (for example to a mounted volume) when running several instances.

**Using SQL Server instead of SQLite:** add `Microsoft.EntityFrameworkCore.SqlServer`, replace `UseSqlite` with `UseSqlServer` in `Program.cs`, remove the decimal→double convention in `ApplicationDbContext.ConfigureConventions`, and regenerate the migrations. The append-only triggers would also need T-SQL equivalents.

---

## Testing

`dotnet test` runs:

- **Unit tests:** lead workflow transitions, opportunity rules (amount, probability, close date, weighted value, reopening), follow-up date rules, every validation attribute and its message, enum tampering, CSV formula-injection escaping.
- **Integration tests** (real app via `WebApplicationFactory`, fresh SQLite database each run), which map to §13 and §17.19:
  - Anonymous users are redirected or get 401.
  - Login and logout work; invalid credentials are rejected.
  - Lockout triggers after 5 failed attempts and is audited.
  - Passwords are stored hashed, and the password policy is enforced on registration.
  - Pages differ by role, and changing a record's URL does not bypass scope.
  - A POST without an anti-forgery token is rejected.
  - Crafted invalid form posts are rejected on the server.
  - API status codes are correct (200/201/204/400/401/403/404/409), and API data is scoped per role.
  - Duplicate customers are blocked.
  - Opportunity and follow-up business rules hold, as does the lead workflow and conversion.
  - Audit entries record old/new values and cannot be modified.
  - Tokens are revoked on logout and on deactivation.
  - Reports render and export to CSV; the OpenAPI document is published.

---

## Design decisions & limitations

- **Delete = deactivate.** Customers, leads, opportunities, follow-ups and activities are soft-deleted, so history and audit stay intact. Only Admins and Managers (within their team) can delete. A customer with open opportunities cannot be deleted (409).
- **Roles are fixed** (Admin, Manager, Sales Executive), one per user, as the spec requires. A Manager's team is defined by the users whose reporting manager they are.
- **Follow-up dates** change only through *Reschedule*, which re-applies the not-before-today rule and is audited.
- **Codes** (`CUS-00001`, `LEA-00001`) are generated sequentially and protected by unique indexes.
- The token revocation list uses `IDistributedCache` (in-memory by default). Swap in Redis for multi-instance deployments.
