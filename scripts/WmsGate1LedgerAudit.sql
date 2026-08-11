SET NOCOUNT ON;

SELECT 'DUPLICATE_LEDGER_IDEMPOTENCY' AS IssueCode,
       IdempotencyKey,
       COUNT_BIG(*) AS DuplicateCount
FROM InventoryTransactions
WHERE NULLIF(LTRIM(RTRIM(IdempotencyKey)), '') IS NOT NULL
GROUP BY IdempotencyKey
HAVING COUNT_BIG(*) > 1;

SELECT 'ORPHAN_LEDGER_REFERENCE' AS IssueCode,
       t.InventoryTransactionId,
       t.VoucherId,
       t.VoucherDetailId,
       t.ItemId,
       t.LocationId,
       t.WarehouseId
FROM InventoryTransactions t
LEFT JOIN Vouchers v ON v.VoucherId = t.VoucherId
LEFT JOIN VoucherDetails vd ON vd.VoucherDetailId = t.VoucherDetailId
LEFT JOIN Items i ON i.ItemId = t.ItemId
LEFT JOIN Locations l ON l.LocationId = t.LocationId
LEFT JOIN Warehouses w ON w.WarehouseId = t.WarehouseId
WHERE (t.VoucherId IS NOT NULL AND v.VoucherId IS NULL)
   OR (t.VoucherDetailId IS NOT NULL AND vd.VoucherDetailId IS NULL)
   OR (t.VoucherDetailId IS NOT NULL AND t.VoucherId IS NOT NULL AND vd.VoucherId <> t.VoucherId)
   OR i.ItemId IS NULL
   OR l.LocationId IS NULL
   OR w.WarehouseId IS NULL;

SELECT 'LEDGER_BALANCE_EQUATION_MISMATCH' AS IssueCode,
       InventoryTransactionId,
       QuantityDelta,
       ReservedDelta,
       AvailableDelta
FROM InventoryTransactions
WHERE ABS((QuantityAfter - QuantityBefore) - QuantityDelta) > 0.0001
   OR ABS((ReservedAfter - ReservedBefore) - ReservedDelta) > 0.0001
   OR ABS((AvailableAfter - AvailableBefore) - AvailableDelta) > 0.0001
   OR ABS((QuantityBefore - ReservedBefore) - AvailableBefore) > 0.0001
   OR ABS((QuantityAfter - ReservedAfter) - AvailableAfter) > 0.0001;

SELECT 'LEDGER_NEGATIVE_AFTER_BALANCE' AS IssueCode,
       InventoryTransactionId,
       TransactionType,
       TransactionGroupKey,
       ReferenceType,
       ReferenceId,
       QuantityAfter,
       ReservedAfter,
       AvailableAfter
FROM InventoryTransactions
WHERE QuantityAfter < 0
   OR ReservedAfter < 0
   OR ReservedAfter > QuantityAfter
   OR AvailableAfter < 0;

SELECT 'REVERSAL_METADATA_INCOMPLETE' AS IssueCode,
       InventoryTransactionId,
       VoucherId,
       TransactionGroupKey
FROM InventoryTransactions
WHERE TransactionGroupKey LIKE 'voucher:%:cancel'
  AND (
        MetadataJson NOT LIKE '%originalInventoryTransactionIds%'
        OR MetadataJson NOT LIKE '%cancelReason%'
      );
