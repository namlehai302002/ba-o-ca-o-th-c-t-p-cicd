/*
Read-only incident trace for the two workflows reported on 2026-07-13.
Every result set contains observations only. No credentials or actor names are selected.
*/

SET NOCOUNT ON;

SELECT 'REPORTED_VOUCHER_STATE' AS IssueCode,
       v.VoucherId, v.VoucherCode, v.VoucherType, v.IsPosted, v.IsCancelled,
       v.FulfillmentStatus, v.WarehouseId, v.OwnerPartnerId,
       COUNT(DISTINCT vd.VoucherDetailId) AS DetailCount,
       COUNT(DISTINCT r.StockReservationId) AS ReservationCount,
       COUNT(DISTINCT pt.PickTaskId) AS PickTaskCount,
       COUNT(DISTINCT tx.InventoryTransactionId) AS LedgerCount
FROM Vouchers v
LEFT JOIN VoucherDetails vd ON vd.VoucherId = v.VoucherId
LEFT JOIN StockReservations r ON r.VoucherId = v.VoucherId
LEFT JOIN PickTasks pt ON pt.VoucherId = v.VoucherId
LEFT JOIN InventoryTransactions tx ON tx.VoucherId = v.VoucherId
WHERE v.VoucherId IN (143, 144)
GROUP BY v.VoucherId, v.VoucherCode, v.VoucherType, v.IsPosted, v.IsCancelled,
         v.FulfillmentStatus, v.WarehouseId, v.OwnerPartnerId;

SELECT 'REPORTED_VOUCHER_LINES' AS IssueCode,
       vd.VoucherId, vd.VoucherDetailId, i.ItemCode, i.ItemName,
       vd.LocationId, l.LocationCode, vd.BaseQty, vd.TransactionQty,
       vd.LotNumber, vd.ExpiryDate, vd.OwnerPartnerId
FROM VoucherDetails vd
JOIN Items i ON i.ItemId = vd.ItemId
LEFT JOIN Locations l ON l.LocationId = vd.LocationId
WHERE vd.VoucherId IN (143, 144)
ORDER BY vd.VoucherId, vd.LineNumber, vd.VoucherDetailId;

SELECT 'REPORTED_INBOUND_LOCATION_CONTENT' AS IssueCode,
       vd.VoucherId, l.LocationId, l.LocationCode,
       i.ItemCode, i.ItemName, il.Quantity, il.ReservedQty,
       il.OwnerPartnerId, il.LotNumber, il.ExpiryDate, il.HoldStatus
FROM VoucherDetails vd
JOIN Locations l ON l.LocationId = vd.LocationId
JOIN ItemLocations il ON il.LocationId = vd.LocationId AND il.Quantity > 0
JOIN Items i ON i.ItemId = il.ItemId
WHERE vd.VoucherId = 143
ORDER BY l.LocationCode, i.ItemCode, il.ItemLocationId;

SELECT 'REPORTED_OUTBOUND_RESERVATIONS' AS IssueCode,
       r.VoucherId, r.StockReservationId, r.VoucherDetailId,
       i.ItemCode, l.LocationCode, r.ReservedQty, r.ConsumedQty,
       r.ReleasedQty, r.Status,
       r.ReservedQty - r.ConsumedQty - r.ReleasedQty AS OpenQty
FROM StockReservations r
JOIN Items i ON i.ItemId = r.ItemId
JOIN Locations l ON l.LocationId = r.LocationId
WHERE r.VoucherId = 144
ORDER BY r.StockReservationId;

SELECT 'REPORTED_OUTBOUND_PICK_STATE' AS IssueCode,
       pt.VoucherId, pt.PickTaskId, pt.TaskCode, i.ItemCode,
       src.LocationCode AS SourceLocationCode,
       pt.TargetQty, pt.PickedQty, pt.Status, pt.PickTaskMode,
       CASE WHEN pt.AssignedTo = v.CreatedBy THEN 1 ELSE 0 END AS AssigneeIsVoucherCreator,
       SUM(ISNULL(a.AllocatedQty, 0)) AS AllocatedQty,
       SUM(ISNULL(a.PickedQty, 0)) AS AllocationPickedQty
FROM PickTasks pt
JOIN Vouchers v ON v.VoucherId = pt.VoucherId
JOIN Items i ON i.ItemId = pt.ItemId
JOIN Locations src ON src.LocationId = pt.SourceLocationId
LEFT JOIN PickTaskAllocations a ON a.PickTaskId = pt.PickTaskId
WHERE pt.VoucherId = 144
GROUP BY pt.VoucherId, pt.PickTaskId, pt.TaskCode, i.ItemCode,
         src.LocationCode, pt.TargetQty, pt.PickedQty, pt.Status, pt.PickTaskMode,
         CASE WHEN pt.AssignedTo = v.CreatedBy THEN 1 ELSE 0 END
ORDER BY pt.PickTaskId;

SELECT 'REPORTED_OUTBOUND_STOCK' AS IssueCode,
       r.VoucherId, i.ItemCode, l.LocationCode,
       il.Quantity, il.ReservedQty,
       il.Quantity - il.ReservedQty AS AvailableQty,
       r.ReservedQty, r.ConsumedQty, r.ReleasedQty
FROM StockReservations r
JOIN ItemLocations il
  ON il.ItemId = r.ItemId
 AND ISNULL(il.OwnerPartnerId, -1) = ISNULL(r.OwnerPartnerId, -1)
 AND il.LocationId = r.LocationId
 AND ISNULL(il.LotNumber, '') = ISNULL(r.LotNumber, '')
 AND ISNULL(il.ExpiryDate, CONVERT(date, '19000101')) = ISNULL(r.ExpiryDate, CONVERT(date, '19000101'))
JOIN Items i ON i.ItemId = r.ItemId
JOIN Locations l ON l.LocationId = r.LocationId
WHERE r.VoucherId = 144
ORDER BY r.StockReservationId;

SELECT 'REPORTED_OUTBOUND_LEDGER' AS IssueCode,
       tx.VoucherId, tx.InventoryTransactionId, tx.TransactionType,
       i.ItemCode, l.LocationCode, tx.QuantityDelta, tx.ReservedDelta,
       tx.QuantityBefore, tx.QuantityAfter,
       tx.ReservedBefore, tx.ReservedAfter,
       tx.ReferenceType, tx.ReferenceId
FROM InventoryTransactions tx
JOIN Items i ON i.ItemId = tx.ItemId
JOIN Locations l ON l.LocationId = tx.LocationId
WHERE tx.VoucherId = 144
ORDER BY tx.InventoryTransactionId;
