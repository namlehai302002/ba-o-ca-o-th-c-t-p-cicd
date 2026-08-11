/*
WMS Pro data-quality audit queries.
Read-only script. Do not run UPDATE/DELETE/MERGE from this file.
Run on staging/admin SQL session with least privilege read access.

Expected use:
  sqlcmd -S <server> -d <database> -i scripts/WmsDataQualityAudit.sql -o artifacts/data-quality/wms-data-quality.txt

Do not commit output if it contains business-sensitive partner/item data.
*/

SET NOCOUNT ON;

PRINT 'WMS_DATA_QUALITY_AUDIT_START';

SELECT 'ITEM_BASE_UOM_INVALID' AS IssueCode, i.ItemId, i.ItemCode, i.BaseUomId
FROM Items i
LEFT JOIN UnitsOfMeasure u ON u.UomId = i.BaseUomId AND u.IsActive = 1
WHERE i.IsActive = 1 AND (i.BaseUomId IS NULL OR u.UomId IS NULL);

SELECT 'UOM_CONVERSION_RATE_INVALID' AS IssueCode, c.ConversionId, c.ItemId, c.FromUomId, c.ToUomId, c.ConversionRate
FROM UnitConversions c
WHERE c.IsActive = 1 AND (c.ConversionRate IS NULL OR c.ConversionRate <= 0);

SELECT 'UOM_CONVERSION_UOM_INACTIVE' AS IssueCode, c.ConversionId, c.ItemId, c.FromUomId, c.ToUomId
FROM UnitConversions c
LEFT JOIN UnitsOfMeasure fu ON fu.UomId = c.FromUomId AND fu.IsActive = 1
LEFT JOIN UnitsOfMeasure tu ON tu.UomId = c.ToUomId AND tu.IsActive = 1
WHERE c.IsActive = 1 AND (fu.UomId IS NULL OR tu.UomId IS NULL);

SELECT 'LOCATION_ORPHAN_OR_INACTIVE_SCOPE' AS IssueCode, il.ItemLocationId, il.ItemId, il.LocationId
FROM ItemLocations il
LEFT JOIN Items i ON i.ItemId = il.ItemId
LEFT JOIN Locations l ON l.LocationId = il.LocationId
LEFT JOIN Zones z ON z.ZoneId = l.ZoneId
LEFT JOIN Warehouses w ON w.WarehouseId = z.WarehouseId
WHERE i.ItemId IS NULL OR l.LocationId IS NULL OR ISNULL(l.IsActive, 0) = 0 OR ISNULL(z.IsActive, 0) = 0 OR ISNULL(w.IsActive, 0) = 0;

;WITH PositiveStockKeys AS
(
    SELECT DISTINCT il.LocationId, il.ItemId, ISNULL(il.OwnerPartnerId, -1) AS OwnerPartnerId
    FROM ItemLocations il
    WHERE il.Quantity > 0.0001
)
SELECT 'LOCATION_MULTIPLE_STOCK_KEYS' AS IssueCode,
       l.LocationId, l.LocationCode, COUNT(*) AS PositiveStockKeyCount
FROM PositiveStockKeys stockKey
JOIN Locations l ON l.LocationId = stockKey.LocationId
GROUP BY l.LocationId, l.LocationCode
HAVING COUNT(*) > 1;

SELECT 'ITEM_LOCATION_NEGATIVE_QTY' AS IssueCode, il.ItemLocationId, il.ItemId, il.LocationId, il.Quantity, il.ReservedQty
FROM ItemLocations il
WHERE il.Quantity < 0 OR il.ReservedQty < 0;

SELECT 'ITEM_LOCATION_RESERVED_EXCEEDS_QTY' AS IssueCode, il.ItemLocationId, il.ItemId, il.LocationId, il.Quantity, il.ReservedQty
FROM ItemLocations il
WHERE il.ReservedQty > il.Quantity;

;WITH ReservationDemand AS
(
    SELECT ItemId, LocationId, OwnerPartnerId, LotNumber, ExpiryDate, ReservedQty - ConsumedQty - ReleasedQty AS OpenQty
    FROM StockReservations
    WHERE Status = 1
    UNION ALL
    SELECT ComponentItemId, SourceLocationId, OwnerPartnerId, LotNumber, ExpiryDate, ReservedQty - ConsumedQty - ReleasedQty
    FROM KittingWorkOrderLines
    WHERE Status = 2 AND SourceLocationId IS NOT NULL
    UNION ALL
    SELECT MaterialItemId, SourceLocationId, OwnerPartnerId, LotNumber, ExpiryDate, ReservedQty - ConsumedQty - ReleasedQty
    FROM VasMaterialLines
    WHERE Status = 2 AND SourceLocationId IS NOT NULL
),
ReservationAgg AS
(
    SELECT ItemId, LocationId, ISNULL(OwnerPartnerId, -1) AS OwnerPartnerId, ISNULL(LotNumber, '') AS LotNumber,
           ISNULL(CONVERT(date, ExpiryDate), '19000101') AS ExpiryDateKey,
           SUM(OpenQty) AS ActiveReserved
    FROM ReservationDemand
    GROUP BY ItemId, LocationId, ISNULL(OwnerPartnerId, -1), ISNULL(LotNumber, ''), ISNULL(CONVERT(date, ExpiryDate), '19000101')
)
SELECT 'ITEM_LOCATION_RESERVED_SOURCE_MISMATCH' AS IssueCode, il.ItemLocationId, il.ItemId, il.LocationId, il.ReservedQty, ISNULL(r.ActiveReserved, 0) AS ActiveReserved
FROM ItemLocations il
LEFT JOIN ReservationAgg r ON r.ItemId = il.ItemId
    AND r.LocationId = il.LocationId
    AND r.OwnerPartnerId = ISNULL(il.OwnerPartnerId, -1)
    AND r.LotNumber = ISNULL(il.LotNumber, '')
    AND r.ExpiryDateKey = ISNULL(CONVERT(date, il.ExpiryDate), '19000101')
WHERE ABS(il.ReservedQty - ISNULL(r.ActiveReserved, 0)) > 0.0001;

SELECT 'CURRENT_STOCK_MISMATCH' AS IssueCode, i.ItemId, i.ItemCode, i.CurrentStock, ISNULL(SUM(il.Quantity), 0) AS ItemLocationQty
FROM Items i
LEFT JOIN ItemLocations il ON il.ItemId = i.ItemId
GROUP BY i.ItemId, i.ItemCode, i.CurrentStock
HAVING ABS(i.CurrentStock - ISNULL(SUM(il.Quantity), 0)) > 0.0001;

SELECT 'TRACKED_LOT_MISSING' AS IssueCode, il.ItemLocationId, i.ItemCode, il.Quantity
FROM ItemLocations il
JOIN Items i ON i.ItemId = il.ItemId
WHERE i.TrackLot = 1 AND il.Quantity > 0 AND (il.LotNumber IS NULL OR LTRIM(RTRIM(il.LotNumber)) = '');

SELECT 'TRACKED_EXPIRY_MISSING' AS IssueCode, il.ItemLocationId, i.ItemCode, il.Quantity
FROM ItemLocations il
JOIN Items i ON i.ItemId = il.ItemId
WHERE i.TrackExpiry = 1 AND il.Quantity > 0 AND il.ExpiryDate IS NULL;

SELECT 'EXPIRY_BEFORE_MFG' AS IssueCode, vd.VoucherDetailId, v.VoucherCode, vd.ItemId, vd.ManufacturingDate, vd.ExpiryDate
FROM VoucherDetails vd
JOIN Vouchers v ON v.VoucherId = vd.VoucherId
WHERE vd.ManufacturingDate IS NOT NULL AND vd.ExpiryDate IS NOT NULL AND vd.ExpiryDate < vd.ManufacturingDate;

SELECT 'SERIAL_ACTIVE_DUPLICATE' AS IssueCode, WarehouseId, OwnerPartnerId, ItemId, SerialCode, COUNT(*) AS DuplicateCount
FROM SerialNumbers
WHERE Status IN (1, 4, 5) AND SerialCode IS NOT NULL AND LTRIM(RTRIM(SerialCode)) <> ''
GROUP BY WarehouseId, OwnerPartnerId, ItemId, SerialCode
HAVING COUNT(*) > 1;

SELECT 'SERIAL_CONSUMED_WITHOUT_TIMESTAMP' AS IssueCode, SerialNumberId, SerialCode, ItemId
FROM SerialNumbers
WHERE Status IN (2) AND ConsumedAt IS NULL;

SELECT 'ACTIVE_RESERVATION_NEGATIVE_OR_OVER_CLOSED' AS IssueCode, StockReservationId, ItemId, LocationId, ReservedQty, ConsumedQty, ReleasedQty
FROM StockReservations
WHERE ReservedQty < 0 OR ConsumedQty < 0 OR ReleasedQty < 0 OR (ConsumedQty + ReleasedQty) > ReservedQty;

SELECT 'VOUCHER_HEADER_LINES_MISMATCH' AS IssueCode, v.VoucherId, v.VoucherCode, v.TotalLines, COUNT(vd.VoucherDetailId) AS ActualLines
FROM Vouchers v
LEFT JOIN VoucherDetails vd ON vd.VoucherId = v.VoucherId
GROUP BY v.VoucherId, v.VoucherCode, v.TotalLines
HAVING v.TotalLines <> COUNT(vd.VoucherDetailId);

SELECT 'POSTED_VOUCHER_WITHOUT_LEDGER' AS IssueCode, v.VoucherId, v.VoucherCode, v.VoucherType
FROM Vouchers v
WHERE v.IsPosted = 1
  AND NOT EXISTS (SELECT 1 FROM InventoryTransactions t WHERE t.VoucherId = v.VoucherId);

SELECT 'OPEN_OUTBOUND_WITH_OVER_RESERVATION' AS IssueCode, r.StockReservationId, r.VoucherId, r.ItemId, r.LocationId, r.ReservedQty, r.ConsumedQty, r.ReleasedQty
FROM StockReservations r
JOIN ItemLocations il ON il.ItemId = r.ItemId
    AND il.LocationId = r.LocationId
    AND ISNULL(il.OwnerPartnerId, -1) = ISNULL(r.OwnerPartnerId, -1)
    AND ISNULL(il.LotNumber, '') = ISNULL(r.LotNumber, '')
    AND ISNULL(CONVERT(date, il.ExpiryDate), '19000101') = ISNULL(CONVERT(date, r.ExpiryDate), '19000101')
WHERE r.Status = 1
  AND (r.ReservedQty - r.ConsumedQty - r.ReleasedQty) > il.Quantity;

PRINT 'WMS_DATA_QUALITY_AUDIT_END';
