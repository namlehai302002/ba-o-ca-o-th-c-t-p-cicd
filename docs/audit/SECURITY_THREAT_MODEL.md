# WMS Security Threat Model

## Assets

- Inventory quantity, reservation and ledger integrity.
- Voucher state, approvals, owner/warehouse isolation and financial values.
- User credentials, sessions, MFA/trusted-device data and permission assignments.
- Uploaded documents, OCR content, exports, audit logs and integration messages.
- Database, backup, DataProtection keys and external-provider credentials.

## Trust Boundaries

1. Browser/PWA ↔ ASP.NET Core MVC/API.
2. Reverse proxy ↔ application forwarded-header boundary.
3. Application ↔ SQL Server hosting database.
4. Application ↔ OCR/email/carrier/webhook/MHE integrations.
5. Background workers ↔ outbox/reconciliation/replenishment tables.
6. Admin/Manager override ↔ warehouse/owner-scoped operational data.

## Current Controls

- Global authentication and unsafe-method antiforgery validation.
- Cookie HttpOnly/SameSite/Secure policy by environment.
- Permission policies with explicit Admin override.
- Warehouse claim at login for non-admin users.
- Optional owner-scope claims and service checks.
- API key validation, rate limiting and request correlation.
- EF parameterization, upload validation paths and safe error conversion.
- AppDbContext audit and inventory ledger generation inside transaction boundaries.
- DataProtection key persistence and structured telemetry.

## Threat Register

| Threat | Current state | Required verification/remediation |
|---|---|---|
| Cross-warehouse/owner data leakage | `PARTIAL`, critical routes verified | Warehouse/owner/API/export/dashboard/file tests plus isolated role E2E pass; production role UAT remains external |
| Excessive DB credential privilege | `CONFIRMED`, external configuration | Define minimum runtime/migration roles; DBA change remains `BLOCKED` pending approval |
| Startup mutates shared DB | `CONFIRMED` | Add tested initialization switch for audit/read-only smoke; preserve production default |
| API allow-anonymous bypass without key check | `VERIFIED` on reflected API surface | API-key boundary contract and direct unauthorized tests pass |
| IDOR/mass assignment | `VERIFIED` on Gate 2 critical surface | Scope tests and forged server-owned field regression pass; repeat when new actions are added |
| CSRF/XSS/file/formula injection | `VERIFIED` on Gate 2 critical surface | Global unsafe-method antiforgery and targeted malicious-input/file/export tests pass |
| Secret leakage in logs/artifacts | `VERIFIED` on current evidence set | Source and generated-evidence exact-value scans return zero; protected appsettings hash is unchanged |
| Session/MFA/trusted-device misuse | `VERIFIED` on current account flow | Expiry/revoke/disabled-user/lockout/cookie tests pass; production UAT remains external |
| Duplicate/replayed commands | `PARTIAL` | Idempotency tests for voucher, OCR/import, outbox and worker paths |
| Public health information disclosure | `UNKNOWN` | Runtime response tests for anonymous `/health` and protected detail endpoints |

## Safety Boundary

No secret value is included in this document. Database or hosting permission changes, secret rotation, production deployment and destructive migration require an external approval checkpoint.
