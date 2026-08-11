# API Integration Enterprise Contracts

Date: 2026-05-20  
System: WMS Pro  
Scope: backward-compatible API, EDI, webhook, and connector contract evidence.  
Secret rule: this document records config key names only and never records secret values.

## Versioning Policy

- Existing clients remain compatible with the current `/api` routes and the `X-API-Key` header.
- The existing `Api:Key` config key remains the source for API-key validation.
- New external contracts should be introduced under explicit version labels such as `/api/v1` or an equivalent documented OpenAPI version.
- Backward-compatible additions may add nullable response fields, optional request fields, or new endpoints.
- Breaking changes require a new version, migration note, deprecation window, owner communication, rollback note, and contract tests.

## Contract Shape Gates

- API authentication errors must preserve the existing error envelope style and must not disclose secret/config values.
- Read endpoints that expose inventory, shipment, billing, integration, analytics, or audit data must declare warehouse scope and owner scope in the scope registry.
- Webhook payloads must use idempotency keys, signatures, retry state, dead-letter state, replay action, and redacted operational evidence.
- Connector health must cover ERP, TMS, OMS, carrier, and MHE integration families.

## Existing Foundation

- `EnterpriseIntegrationService.BuildOpenApiContract()` publishes a v1-style contract foundation.
- `EdiMessage`, `WebhookSubscription`, `WebhookDelivery`, `EnterpriseConnector`, and `EnterpriseConnectorDelivery` provide persistence for integration lifecycle evidence.
- `ApiIntegrationController` preserves `X-API-Key` compatibility and exposes EDI/webhook integration actions.

## Acceptance

- Static tests must assert `X-API-Key`, `Api:Key`, version policy, EDI contract coverage, webhook retry/dead-letter/replay, safe redaction language, and connector health families.
- Staging evidence must be captured in the release evidence pack before claiming production readiness.

## Tier-1 Production Evidence Matrix

| Family | Required payloads | Required controls |
|---|---|---|
| ERP | Item, UOM, supplier, customer, purchase order, sales order, inventory transaction and reconciliation. | Auth, warehouse/owner scope, idempotency key, retry, dead-letter, audit trail and rollback note. |
| TMS | Load, route, shipment, tracking, delivery confirmation and delivery exception. | Duplicate status protection, carrier/TMS outage retry and stock-safe reconciliation. |
| OMS | Order import, allocation request, partial fulfillment, cancellation and backorder. | Late cancellation handling, duplicate order guard, audit trail and business-owner approval. |
| MHE/WCS | Induction, pick confirmation, pack confirmation, exception and heartbeat. | Sequence guard, replay protection, operator recovery and dead-letter review. |
| Carrier | Rate, label, manifest, tracking and delivery exception. | Label traceability to voucher/package/load, manifest close audit and retry/dead-letter evidence. |

Production Tier-1 integration can be marked complete only when sample request/response payloads, redacted auth proof, retry/dead-letter proof, rollback proof and owner sign-off exist for every family above.

