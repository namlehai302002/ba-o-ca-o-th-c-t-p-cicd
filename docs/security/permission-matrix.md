# Permission Matrix Evidence Index

The current canonical role and permission matrix is [`docs/ROLE_PERMISSION_MATRIX.md`](../ROLE_PERMISSION_MATRIX.md).

Automated and runtime evidence:

- `AuthorizationMatrixTests` validates critical role groups, sensitive action policies, Admin policy override and server-owned voucher-state binding.
- `RbacSeed_ShouldGrantAdminEveryDefinedPermissionAndCreateAllRoles` validates all nine roles and the complete Admin grant set.
- `scripts/WmsRbacReadOnlyAudit.sql` reconciles the expected grant matrix with the hosting database through read-only queries.
- `artifacts/full-audit/GATE0_STATE_MACHINE_PERMISSION_EVIDENCE_2026_07_11.md` records current command results and remaining boundaries.
- `artifacts/full-audit/test-results/gate2-authorization-scope-targeted-20260713.trx` records 315 passing authorization/API/scope tests on the current build.
- `artifacts/data-quality/wms-gate2-rbac-readonly-audit-20260713.txt` records zero missing/unexpected/duplicate RBAC grants on the current database target.

Authenticated direct-route role testing has local isolated evidence for seven operational roles under `artifacts/role-e2e/`; its 14 menu/direct-route assertions passed on 2026-07-13. Cross-device evidence remains under `artifacts/ui-cross-device/`. Production role UAT remains an external sign-off boundary and is not inferred from local fixtures.
