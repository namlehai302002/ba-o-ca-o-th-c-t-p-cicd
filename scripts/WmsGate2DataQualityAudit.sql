/*
Gate 2 read-only data-quality audit.
Every result set contains findings only; Count = 0 is the PASS condition.
The script intentionally contains no write or DDL statement.
*/

SET NOCOUNT ON;

SELECT 'RESERVATION_CLOSED_QTY_INVALID' AS IssueCode,
       r.StockReservationId, r.VoucherId, r.VoucherDetailId,
       r.ReservedQty, r.ConsumedQty, r.ReleasedQty, r.Status
FROM StockReservations r
WHERE r.ReservedQty < 0
   OR r.ConsumedQty < 0
   OR r.ReleasedQty < 0
   OR r.ConsumedQty + r.ReleasedQty > r.ReservedQty;

SELECT 'LEDGER_ORPHAN_REFERENCE' AS IssueCode,
       t.InventoryTransactionId, t.VoucherId, t.VoucherDetailId,
       t.StockReservationId, t.ItemId, t.LocationId
FROM InventoryTransactions t
LEFT JOIN Vouchers v ON v.VoucherId = t.VoucherId
LEFT JOIN VoucherDetails vd ON vd.VoucherDetailId = t.VoucherDetailId
LEFT JOIN StockReservations r ON r.StockReservationId = t.StockReservationId
LEFT JOIN Items i ON i.ItemId = t.ItemId
LEFT JOIN Locations l ON l.LocationId = t.LocationId
WHERE (t.VoucherId IS NOT NULL AND v.VoucherId IS NULL)
   OR (t.VoucherDetailId IS NOT NULL AND vd.VoucherDetailId IS NULL)
   OR (t.StockReservationId IS NOT NULL AND r.StockReservationId IS NULL)
   OR i.ItemId IS NULL
   OR l.LocationId IS NULL;

SELECT 'LEDGER_DUPLICATE_IDEMPOTENCY_KEY' AS IssueCode,
       t.IdempotencyKey, COUNT(*) AS DuplicateCount
FROM InventoryTransactions t
GROUP BY t.IdempotencyKey
HAVING COUNT(*) > 1;

SELECT 'PHYSICAL_LEDGER_LINKED_TO_UNPOSTED_VOUCHER' AS IssueCode,
       t.InventoryTransactionId, t.TransactionType, t.VoucherId,
       t.QuantityDelta, v.IsPosted, v.IsCancelled
FROM InventoryTransactions t
JOIN Vouchers v ON v.VoucherId = t.VoucherId
WHERE ABS(t.QuantityDelta) > 0.0001
  AND v.IsPosted = 0
  AND v.IsCancelled = 0;

;WITH LedgerBalance AS
(
    SELECT t.WarehouseId,
           ISNULL(t.OwnerPartnerId, -1) AS OwnerPartnerId,
           t.ItemId,
           t.LocationId,
           ISNULL(t.LotNumber, '') AS LotNumber,
           ISNULL(CONVERT(date, t.ExpiryDate), CONVERT(date, '19000101')) AS ExpiryDate,
           SUM(t.QuantityDelta) AS LedgerQty,
           SUM(t.ReservedDelta) AS LedgerReservedQty
    FROM InventoryTransactions t
    GROUP BY t.WarehouseId, ISNULL(t.OwnerPartnerId, -1), t.ItemId, t.LocationId,
             ISNULL(t.LotNumber, ''), ISNULL(CONVERT(date, t.ExpiryDate), CONVERT(date, '19000101'))
),
CurrentBalance AS
(
    SELECT z.WarehouseId,
           ISNULL(il.OwnerPartnerId, -1) AS OwnerPartnerId,
           il.ItemId,
           il.LocationId,
           ISNULL(il.LotNumber, '') AS LotNumber,
           ISNULL(CONVERT(date, il.ExpiryDate), CONVERT(date, '19000101')) AS ExpiryDate,
           SUM(il.Quantity) AS CurrentQty,
           SUM(il.ReservedQty) AS CurrentReservedQty
    FROM ItemLocations il
    JOIN Locations l ON l.LocationId = il.LocationId
    JOIN Zones z ON z.ZoneId = l.ZoneId
    GROUP BY z.WarehouseId, ISNULL(il.OwnerPartnerId, -1), il.ItemId, il.LocationId,
             ISNULL(il.LotNumber, ''), ISNULL(CONVERT(date, il.ExpiryDate), CONVERT(date, '19000101'))
)
SELECT 'LEDGER_CURRENT_BALANCE_MISMATCH' AS IssueCode,
       COALESCE(lb.WarehouseId, cb.WarehouseId) AS WarehouseId,
       COALESCE(lb.OwnerPartnerId, cb.OwnerPartnerId) AS OwnerPartnerId,
       COALESCE(lb.ItemId, cb.ItemId) AS ItemId,
       COALESCE(lb.LocationId, cb.LocationId) AS LocationId,
       COALESCE(lb.LotNumber, cb.LotNumber) AS LotNumber,
       COALESCE(lb.ExpiryDate, cb.ExpiryDate) AS ExpiryDate,
       ISNULL(lb.LedgerQty, 0) AS LedgerQty,
       ISNULL(cb.CurrentQty, 0) AS CurrentQty,
       ISNULL(lb.LedgerReservedQty, 0) AS LedgerReservedQty,
       ISNULL(cb.CurrentReservedQty, 0) AS CurrentReservedQty
FROM LedgerBalance lb
FULL OUTER JOIN CurrentBalance cb
    ON cb.WarehouseId = lb.WarehouseId
   AND cb.OwnerPartnerId = lb.OwnerPartnerId
   AND cb.ItemId = lb.ItemId
   AND cb.LocationId = lb.LocationId
   AND cb.LotNumber = lb.LotNumber
   AND cb.ExpiryDate = lb.ExpiryDate
WHERE ABS(ISNULL(lb.LedgerQty, 0) - ISNULL(cb.CurrentQty, 0)) > 0.0001
   OR ABS(ISNULL(lb.LedgerReservedQty, 0) - ISNULL(cb.CurrentReservedQty, 0)) > 0.0001;

SELECT 'LEDGER_ARITHMETIC_OR_DIRECTION_INVALID' AS IssueCode,
       t.InventoryTransactionId, t.TransactionType,
       t.QuantityDelta, t.ReservedDelta, t.AvailableDelta,
       t.QuantityBefore, t.QuantityAfter,
       t.ReservedBefore, t.ReservedAfter,
       t.AvailableBefore, t.AvailableAfter
FROM InventoryTransactions t
WHERE t.TransactionType NOT BETWEEN 1 AND 17
   OR ABS((t.QuantityAfter - t.QuantityBefore) - t.QuantityDelta) > 0.0001
   OR ABS((t.ReservedAfter - t.ReservedBefore) - t.ReservedDelta) > 0.0001
   OR ABS((t.AvailableAfter - t.AvailableBefore) - t.AvailableDelta) > 0.0001
   OR ABS(t.AvailableDelta - (t.QuantityDelta - t.ReservedDelta)) > 0.0001
   OR t.QuantityAfter < 0
   OR t.ReservedAfter < 0
   OR t.ReservedAfter > t.QuantityAfter
   OR (t.TransactionType IN (1, 2, 10, 16) AND t.QuantityDelta < 0)
   OR (t.TransactionType IN (7, 11, 15, 17) AND t.QuantityDelta > 0);

SELECT 'EXPIRED_LOT_STILL_AVAILABLE' AS IssueCode,
       il.ItemLocationId, il.ItemId, il.LocationId, il.LotNumber,
       il.ExpiryDate, il.Quantity, il.ReservedQty, il.HoldStatus
FROM ItemLocations il
WHERE il.ExpiryDate < CONVERT(date, SYSDATETIMEOFFSET() AT TIME ZONE 'SE Asia Standard Time')
  AND il.HoldStatus = 1
  AND il.Quantity - il.ReservedQty > 0.0001;

SELECT 'UOM_CONVERSION_DUPLICATE_OR_INVALID' AS IssueCode,
       ISNULL(c.ItemId, -1) AS ItemId, c.FromUomId, c.ToUomId,
       COUNT(*) AS ActiveCount, MIN(c.ConversionRate) AS MinRate, MAX(c.ConversionRate) AS MaxRate
FROM UnitConversions c
WHERE c.IsActive = 1
GROUP BY ISNULL(c.ItemId, -1), c.FromUomId, c.ToUomId
HAVING COUNT(*) > 1
    OR MIN(c.ConversionRate) <= 0
    OR MAX(c.ConversionRate) <= 0
    OR (c.FromUomId = c.ToUomId AND (MIN(c.ConversionRate) <> 1 OR MAX(c.ConversionRate) <> 1));

SELECT 'VOUCHER_LINE_UOM_OR_QUANTITY_INVALID' AS IssueCode,
       vd.VoucherDetailId, vd.VoucherId, vd.ItemId,
       vd.TransactionQty, vd.TransactionUomId, i.BaseUomId,
       vd.ConversionRate, vd.BaseQty, vd.UnitPrice, vd.LineAmount
FROM VoucherDetails vd
JOIN Items i ON i.ItemId = vd.ItemId
LEFT JOIN UnitsOfMeasure u ON u.UomId = vd.TransactionUomId
WHERE vd.TransactionQty <= 0
   OR vd.ConversionRate <= 0
   OR vd.BaseQty <= 0
   OR u.UomId IS NULL
   OR ABS(vd.BaseQty - (vd.TransactionQty * vd.ConversionRate)) > 0.0001
   OR ABS(vd.LineAmount - (vd.TransactionQty * vd.UnitPrice)) > 0.01
   OR (vd.TransactionUomId <> i.BaseUomId AND NOT EXISTS
       (
           SELECT 1
           FROM UnitConversions c
           WHERE c.IsActive = 1
             AND c.FromUomId = vd.TransactionUomId
             AND c.ToUomId = i.BaseUomId
             AND (c.ItemId = vd.ItemId OR c.ItemId IS NULL)
       ));

SELECT 'VOUCHER_TOTAL_ROUNDING_MISMATCH' AS IssueCode,
       v.VoucherId, v.VoucherCode, v.TotalAmount,
       ISNULL(SUM(vd.LineAmount), 0) AS CalculatedTotal
FROM Vouchers v
LEFT JOIN VoucherDetails vd ON vd.VoucherId = v.VoucherId
GROUP BY v.VoucherId, v.VoucherCode, v.TotalAmount
HAVING ABS(v.TotalAmount - ISNULL(SUM(vd.LineAmount), 0)) > 0.01;

SELECT 'VOUCHER_RESERVATION_STATE_MISMATCH' AS IssueCode,
       v.VoucherId, v.VoucherCode, v.IsPosted, v.IsCancelled,
       r.StockReservationId, r.Status, r.ReservedQty, r.ConsumedQty, r.ReleasedQty
FROM Vouchers v
JOIN StockReservations r ON r.VoucherId = v.VoucherId
WHERE (v.IsCancelled = 1 AND r.Status = 1)
   OR (v.IsPosted = 1 AND r.Status = 1)
   OR (r.Status <> 1 AND ABS(r.ReservedQty - r.ConsumedQty - r.ReleasedQty) > 0.0001);
