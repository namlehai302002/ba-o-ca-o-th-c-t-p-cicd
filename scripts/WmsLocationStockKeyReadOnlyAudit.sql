/*
  Read-only diagnostic for locations that contain more than one positive
  item/owner stock key. This script never mutates warehouse data.
*/

SET NOCOUNT ON;

;WITH PositiveStockKeys AS
(
    SELECT
        il.LocationId,
        il.ItemId,
        ISNULL(il.OwnerPartnerId, -1) AS OwnerPartnerId,
        SUM(il.Quantity) AS Quantity
    FROM ItemLocations il
    WHERE il.Quantity > 0.0001
    GROUP BY il.LocationId, il.ItemId, ISNULL(il.OwnerPartnerId, -1)
),
ConflictingLocations AS
(
    SELECT LocationId
    FROM PositiveStockKeys
    GROUP BY LocationId
    HAVING COUNT(*) > 1
)
SELECT
    'LOCATION_STOCK_KEY_DETAIL' AS IssueCode,
    l.LocationId,
    l.LocationCode,
    i.ItemId,
    i.ItemCode,
    NULLIF(stockKey.OwnerPartnerId, -1) AS OwnerPartnerId,
    stockKey.Quantity
FROM PositiveStockKeys stockKey
JOIN ConflictingLocations conflict ON conflict.LocationId = stockKey.LocationId
JOIN Locations l ON l.LocationId = stockKey.LocationId
JOIN Items i ON i.ItemId = stockKey.ItemId
ORDER BY l.LocationCode, i.ItemCode, stockKey.OwnerPartnerId;
