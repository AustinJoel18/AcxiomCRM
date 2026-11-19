# AcxiomCRM REST API

Base URL: `http://localhost:5080`. All bodies are JSON. Enums are sent as strings (for example `"Active"`), and numeric enum values are rejected. Interactive documentation is at `/swagger` in Development.

## Authentication

1. `POST /api/auth/login` returns a JWT.
2. Send it on every call as `Authorization: Bearer <accessToken>`.

Browser sessions (the auth cookie) are also accepted. State-changing calls made with the cookie must include an `X-CSRF-TOKEN` header.

```http
POST /api/auth/login
{ "userNameOrEmail": "sales@acxiomcrm.local", "password": "…" }

200 OK
{ "accessToken": "eyJ…", "tokenType": "Bearer", "expiresAt": "2026-11-19T14:10:00Z",
  "user": { "id": "…", "fullName": "Vikram Singh", "email": "sales@acxiomcrm.local", "role": "SalesExecutive" } }
```

| Endpoint | Purpose | Access |
|---|---|---|
| `POST /api/auth/login` | Issue token (5 failures lock the account; rate limited per IP → 429) | Public |
| `POST /api/auth/logout` | Revoke the current token (204) | Authenticated |
| `GET /api/auth/me` | Current user (no hashes, stamps or tokens) | Authenticated |

## Data scope

Every endpoint requires one of the three CRM roles. Results are scoped automatically:

- **Admin:** all records.
- **Manager:** own records plus their team's.
- **Sales Executive:** own/assigned records only.

A record outside your scope returns **403**; a record that doesn't exist returns **404**.

## Resources

| Method | Endpoint | Purpose | Success |
|---|---|---|---|
| GET | `/api/customers?search=&status=&assignedToId=&page=1&pageSize=10&sort=name&dir=asc` | List/search customers | 200 `PagedResult<CustomerDto>` |
| GET | `/api/customers/{id}` | Get customer | 200 |
| POST | `/api/customers` | Create customer | 201 + `Location` |
| PUT | `/api/customers/{id}` | Update customer | 200 |
| DELETE | `/api/customers/{id}` | Deactivate (soft delete), Admin/Manager only | 204 |
| GET | `/api/leads?search=&status=&source=&assignedToId=` | List/search leads | 200 |
| GET | `/api/leads/{id}` | Get lead (includes `allowedNextStatuses`, `canConvert`) | 200 |
| POST | `/api/leads` | Create lead (initial status New/Contacted/Qualified) | 201 |
| PUT | `/api/leads/{id}` | Update lead (status must follow the workflow) | 200 |
| POST | `/api/leads/{id}/status` | Change status `{ "status": "Contacted", "remarks": "…" }` | 200 |
| POST | `/api/leads/{id}/convert` | Convert a Qualified lead (see below) | 200 |
| DELETE | `/api/leads/{id}` | Deactivate, Admin/Manager only | 204 |
| GET | `/api/opportunities?search=&stage=&status=&customerId=&assignedToId=` | List/search opportunities | 200 |
| GET | `/api/opportunities/{id}` | Get opportunity (includes `weightedAmount`) | 200 |
| POST | `/api/opportunities` | Create opportunity | 201 |
| PUT | `/api/opportunities/{id}` | Update opportunity | 200 |
| POST | `/api/opportunities/{id}/stage` | Move stage `{ "stage": "Won", "closeReason": "…" }` | 200 |
| DELETE | `/api/opportunities/{id}` | Deactivate, Admin/Manager only | 204 |
| GET | `/api/followups?view=pending\|overdue\|today\|upcoming&status=&assignedToId=&dateFrom=&dateTo=` | List follow-ups | 200 |
| GET | `/api/followups/{id}` | Get follow-up | 200 |
| POST | `/api/followups` | Schedule follow-up | 201 |
| PUT | `/api/followups/{id}` | Edit subject/type/remarks/related/assignee | 200 |
| POST | `/api/followups/{id}/complete` | `{ "outcome": "…" }`: logs an activity; a New lead becomes Contacted | 200 |
| POST | `/api/followups/{id}/reschedule` | `{ "newDate": "2026-11-25T11:00", "reason": "…" }` | 200 |
| POST | `/api/followups/{id}/missed` · `/cancel` | Status changes | 200 |
| GET | `/api/reports/pipeline?ownerId=&from=&to=` | Stage-wise and owner-wise pipeline (scoped) | 200 `PipelineReportDto` |
| GET | `/api/reports/dashboard?period=month` | Dashboard KPIs and chart series (scoped) | 200 |

Paging: `pageSize` is capped at 100. Unknown `sort` keys fall back to the default order.

## Request models

**CustomerSaveRequest**

```json
{ "customerName": "Amit Rao", "email": "amit@tata.in", "phone": "9876543210", "companyName": "Tata Lumen",
  "address": "10 MG Road", "city": "Mumbai", "state": "Maharashtra", "status": "Active", "notes": null, "assignedToId": null }
```

- Required: `customerName` (2–100 characters), `email`, `phone` (10-digit mobile), `status` (Prospect/Active/Inactive).
- Email, phone and name+company must be unique, otherwise **409**.
- `assignedToId` is optional and defaults to you. It must be an active user within your scope.

**LeadSaveRequest:** `leadName`*, `email`/`phone` (at least one), `companyName`, `source`* (Website, Referral, Campaign, ColdCall, Event, SocialMedia, Partner, Other), `status`* (New, Contacted, Qualified, Unqualified, Converted, Lost), `priority`* (Low/Medium/High), `expectedValue` (0–100,000,000), `notes`, `assignedToId`.

Status workflow: New → Contacted/Unqualified/Lost; Contacted → Qualified/Unqualified/Lost; Qualified → Contacted/Unqualified/Lost (or **Convert**); Unqualified → Contacted/Lost. Converted and Lost are final.

**LeadConvertRequest**

```json
{ "existingCustomerId": null,
  "customer": { "customerName": "…", "email": "…", "phone": "…", "status": "Active" },
  "opportunity": { "opportunityName": "…", "amount": 150000, "probability": 30, "expectedCloseDate": "2026-12-31" } }
```

Supply either `existingCustomerId` or `customer`. `opportunity` is optional. The response is `{ "leadId", "customerId", "customerCode", "opportunityId" }`.

**OpportunitySaveRequest:** `opportunityName`*, `customerId`* (in scope), `leadId`, `amount`*, `stage`* (Qualification, Proposal, Negotiation, Won, Lost), `probability`* (0–100), `expectedCloseDate`*, `source`, `notes`, `closeReason` (required for Lost), `assignedToId`.

- Active stages need `amount > 0` and a close date of today or later. The amount can never be negative.
- Won sets probability to 100 and Lost sets it to 0. Closed deals cannot be reopened.
- `weightedAmount = amount × probability / 100`.

**FollowUpCreateRequest:** `subject`*, `followUpType`* (Call, Meeting, Email, Visit, Task), `followUpDate`* (not before today), `relatedType`* (Customer/Lead/Opportunity) and `relatedId`* (must be in scope), `remarks`, `assignedToId`.

## Errors

All errors use RFC 7807 `application/problem+json` and never contain stack traces or database details.

| Status | When |
|---|---|
| 400 | Validation or business-rule failure. `errors` maps field to messages, for example `{"Amount":["Opportunity Amount must be greater than 0."]}` |
| 401 | Missing, expired or revoked token; invalid credentials; locked account |
| 403 | Role not permitted (for example a Sales Executive calling DELETE), record outside your scope, or password change required |
| 404 | Record does not exist |
| 409 | Duplicate customer (email, phone or name+company), or deleting a customer with open opportunities |
| 429 | Too many login attempts |
| 500 | Unexpected error (`detail` carries a trace reference only) |

```json
{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1", "title": "One or more validation errors occurred.", "status": 400,
  "errors": { "Probability": ["Probability must be between 0 and 100."], "ExpectedCloseDate": ["Expected Close Date cannot be in the past."] },
  "traceId": "00-…" }
```
