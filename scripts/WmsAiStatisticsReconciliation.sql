/*
AI-1 read-only reconciliation for STAT-01..07.
All quantity comparisons remain at a compatible inventory or line grain.
The script intentionally performs no write or schema operation.
*/

SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

;WITH Eligible AS
(
    SELECT line.SystemQty,
           line.CountedQty,
           COALESCE(line.CountedQty, 0) - line.SystemQty AS CalculatedVariance
    FROM StockCountLines AS line
    INNER JOIN StockCountSheets AS sheet
        ON sheet.StockCountSheetId = line.StockCountSheetId
    WHERE sheet.Status = 4
      AND line.CountedQty IS NOT NULL
)
SELECT 'STAT01_COUNT_ACCURACY' AS IssueCode,
       COUNT_BIG(*) AS EligibleApprovedLines,
       COALESCE(SUM(CASE WHEN ABS(CalculatedVariance) <= 0.0001 THEN 1 ELSE 0 END), 0) AS ExactLines,
       COALESCE(SUM(CASE WHEN CalculatedVariance > 0.0001 THEN 1 ELSE 0 END), 0) AS SurplusLines,
       COALESCE(SUM(CASE WHEN CalculatedVariance < -0.0001 THEN 1 ELSE 0 END), 0) AS ShortageLines,
       CAST(CASE WHEN COUNT_BIG(*) = 0 THEN NULL
            ELSE 100.0 * SUM(CASE WHEN ABS(CalculatedVariance) <= 0.0001 THEN 1 ELSE 0 END) / COUNT_BIG(*)
       END AS decimal(9,4)) AS LineAccuracyPercent,
       CAST(CASE
            WHEN COALESCE(SUM(CASE WHEN ABS(SystemQty) >= ABS(COALESCE(CountedQty, 0))
                                   THEN ABS(SystemQty) ELSE ABS(COALESCE(CountedQty, 0)) END), 0) <= 0.0001
                THEN NULL
            ELSE 100.0 * (1.0 -
                 SUM(ABS(CalculatedVariance)) /
                 SUM(CASE WHEN ABS(SystemQty) >= ABS(COALESCE(CountedQty, 0))
                          THEN ABS(SystemQty) ELSE ABS(COALESCE(CountedQty, 0)) END))
       END AS decimal(9,4)) AS QuantityAccuracyPercent
FROM Eligible;

;WITH LedgerBalance AS
(
    SELECT transactionRow.WarehouseId,
           ISNULL(transactionRow.OwnerPartnerId, -1) AS OwnerPartnerId,
           transactionRow.ItemId,
           transactionRow.LocationId,
           ISNULL(transactionRow.LotNumber, '') AS LotNumber,
           ISNULL(CONVERT(date, transactionRow.ExpiryDate), CONVERT(date, '19000101')) AS ExpiryDate,
           SUM(transactionRow.QuantityDelta) AS LedgerQty,
           SUM(transactionRow.ReservedDelta) AS LedgerReservedQty
    FROM InventoryTransactions AS transactionRow
    GROUP BY transactionRow.WarehouseId,
             ISNULL(transactionRow.OwnerPartnerId, -1),
             transactionRow.ItemId,
             transactionRow.LocationId,
             ISNULL(transactionRow.LotNumber, ''),
             ISNULL(CONVERT(date, transactionRow.ExpiryDate), CONVERT(date, '19000101'))
),
CurrentBalance AS
(
    SELECT zone.WarehouseId,
           ISNULL(itemLocation.OwnerPartnerId, -1) AS OwnerPartnerId,
           itemLocation.ItemId,
           itemLocation.LocationId,
           ISNULL(itemLocation.LotNumber, '') AS LotNumber,
           ISNULL(CONVERT(date, itemLocation.ExpiryDate), CONVERT(date, '19000101')) AS ExpiryDate,
           SUM(itemLocation.Quantity) AS CurrentQty,
           SUM(itemLocation.ReservedQty) AS CurrentReservedQty
    FROM ItemLocations AS itemLocation
    INNER JOIN Locations AS location ON location.LocationId = itemLocation.LocationId
    INNER JOIN Zones AS zone ON zone.ZoneId = location.ZoneId
    GROUP BY zone.WarehouseId,
             ISNULL(itemLocation.OwnerPartnerId, -1),
             itemLocation.ItemId,
             itemLocation.LocationId,
             ISNULL(itemLocation.LotNumber, ''),
             ISNULL(CONVERT(date, itemLocation.ExpiryDate), CONVERT(date, '19000101'))
),
Compared AS
(
    SELECT ISNULL(ledger.LedgerQty, 0) AS LedgerQty,
           ISNULL(currentBalance.CurrentQty, 0) AS CurrentQty,
           ISNULL(ledger.LedgerReservedQty, 0) AS LedgerReservedQty,
           ISNULL(currentBalance.CurrentReservedQty, 0) AS CurrentReservedQty
    FROM LedgerBalance AS ledger
    FULL OUTER JOIN CurrentBalance AS currentBalance
        ON currentBalance.WarehouseId = ledger.WarehouseId
       AND currentBalance.OwnerPartnerId = ledger.OwnerPartnerId
       AND currentBalance.ItemId = ledger.ItemId
       AND currentBalance.LocationId = ledger.LocationId
       AND currentBalance.LotNumber = ledger.LotNumber
       AND currentBalance.ExpiryDate = ledger.ExpiryDate
)
SELECT 'STAT02_LEDGER_CURRENT_RECONCILIATION' AS IssueCode,
       COUNT_BIG(*) AS InventoryBucketCount,
       COALESCE(SUM(CASE WHEN ABS(LedgerQty - CurrentQty) > 0.0001 THEN 1 ELSE 0 END), 0) AS QuantityMismatchBuckets,
       COALESCE(SUM(CASE WHEN ABS(LedgerReservedQty - CurrentReservedQty) > 0.0001 THEN 1 ELSE 0 END), 0) AS ReservationMismatchBuckets,
       COALESCE(SUM(ABS(LedgerQty - CurrentQty)), 0) AS AbsoluteQuantityMismatch,
       COALESCE(SUM(ABS(LedgerReservedQty - CurrentReservedQty)), 0) AS AbsoluteReservationMismatch
FROM Compared;

;WITH CurrentStock AS
(
    SELECT itemLocation.ItemId,
           SUM(itemLocation.Quantity) AS CurrentQty
    FROM ItemLocations AS itemLocation
    GROUP BY itemLocation.ItemId
    HAVING SUM(itemLocation.Quantity) > 0.0001
),
LastMovement AS
(
    SELECT transactionRow.ItemId,
           MAX(CASE WHEN transactionRow.TransactionType = 2 AND transactionRow.QuantityDelta > 0
                    THEN transactionRow.TransactionAt END) AS LastReceiptAt,
           MAX(CASE WHEN transactionRow.TransactionType IN (7, 11, 15, 17) AND transactionRow.QuantityDelta < 0
                    THEN transactionRow.TransactionAt END) AS LastOutboundAt
    FROM InventoryTransactions AS transactionRow
    GROUP BY transactionRow.ItemId
),
Profile AS
(
    SELECT stock.ItemId,
           movement.LastReceiptAt,
           movement.LastOutboundAt,
           item.UnitCost
    FROM CurrentStock AS stock
    INNER JOIN Items AS item ON item.ItemId = stock.ItemId
    LEFT JOIN LastMovement AS movement ON movement.ItemId = stock.ItemId
)
SELECT 'STAT03_INVENTORY_AGE_SOURCE' AS IssueCode,
       COUNT_BIG(*) AS StockedSkuCount,
       COALESCE(SUM(CASE WHEN LastOutboundAt IS NULL THEN 1 ELSE 0 END), 0) AS NeverOutboundSkuCount,
       COALESCE(SUM(CASE WHEN LastOutboundAt < DATEADD(day, -90, CONVERT(date, SYSDATETIMEOFFSET() AT TIME ZONE 'SE Asia Standard Time')) THEN 1 ELSE 0 END), 0) AS NoOutboundOver90DaysSkuCount,
       COALESCE(SUM(CASE WHEN LastReceiptAt >= DATEADD(day, -30, CONVERT(date, SYSDATETIMEOFFSET() AT TIME ZONE 'SE Asia Standard Time'))
                         AND (LastOutboundAt IS NULL OR LastOutboundAt < DATEADD(day, -90, CONVERT(date, SYSDATETIMEOFFSET() AT TIME ZONE 'SE Asia Standard Time')))
                    THEN 1 ELSE 0 END), 0) AS RecentReceiptButStillSlowSkuCount,
       COALESCE(SUM(CASE WHEN UnitCost <= 0 THEN 1 ELSE 0 END), 0) AS MissingValuationSkuCount
FROM Profile;

;WITH PositiveInventory AS
(
    SELECT itemLocation.ItemLocationId,
           itemLocation.ItemId,
           itemLocation.ExpiryDate,
           itemLocation.HoldStatus,
           item.TrackExpiry
    FROM ItemLocations AS itemLocation
    INNER JOIN Items AS item ON item.ItemId = itemLocation.ItemId
    WHERE itemLocation.Quantity - itemLocation.ReservedQty > 0.0001
)
SELECT 'STAT04_EXPIRY_SOURCE_QUALITY' AS IssueCode,
       COUNT_BIG(*) AS AvailableInventoryRows,
       COALESCE(SUM(CASE WHEN TrackExpiry = 1 AND ExpiryDate IS NULL THEN 1 ELSE 0 END), 0) AS TrackedExpiryMissingRows,
       COALESCE(SUM(CASE WHEN ExpiryDate < CONVERT(date, SYSDATETIMEOFFSET() AT TIME ZONE 'SE Asia Standard Time') THEN 1 ELSE 0 END), 0) AS ExpiredRows,
       COALESCE(SUM(CASE WHEN ExpiryDate >= CONVERT(date, SYSDATETIMEOFFSET() AT TIME ZONE 'SE Asia Standard Time')
                         AND ExpiryDate < DATEADD(day, 30, CONVERT(date, SYSDATETIMEOFFSET() AT TIME ZONE 'SE Asia Standard Time')) THEN 1 ELSE 0 END), 0) AS ExpiringWithin30DaysRows,
       COALESCE(SUM(CASE WHEN ExpiryDate < CONVERT(date, SYSDATETIMEOFFSET() AT TIME ZONE 'SE Asia Standard Time')
                         AND HoldStatus = 1 THEN 1 ELSE 0 END), 0) AS ExpiredStillAvailableRows
FROM PositiveInventory;

;WITH InboundBase AS
(
    SELECT voucher.VoucherId,
           COALESCE(voucher.DockArrivalAt, voucher.GateInAt) AS ArrivalAt,
           COALESCE(voucher.ReceivedAt, voucher.UnloadStartAt) AS ReceiveStartAt,
           voucher.CompletedAt,
           CASE WHEN COALESCE(voucher.DockArrivalAt, voucher.GateInAt) IS NULL
                     OR COALESCE(voucher.ReceivedAt, voucher.UnloadStartAt) IS NULL
                     OR voucher.CompletedAt IS NULL THEN 1 ELSE 0 END AS MissingMilestone,
           CASE WHEN COALESCE(voucher.DockArrivalAt, voucher.GateInAt) IS NOT NULL
                     AND COALESCE(voucher.ReceivedAt, voucher.UnloadStartAt) IS NOT NULL
                     AND voucher.CompletedAt IS NOT NULL
                     AND (COALESCE(voucher.DockArrivalAt, voucher.GateInAt) > COALESCE(voucher.ReceivedAt, voucher.UnloadStartAt)
                          OR COALESCE(voucher.ReceivedAt, voucher.UnloadStartAt) > voucher.CompletedAt)
                THEN 1 ELSE 0 END AS InvalidMilestoneOrder
    FROM Vouchers AS voucher
    WHERE voucher.IsCancelled = 0
      AND voucher.IsPosted = 1
      AND voucher.VoucherType IN (1, 4, 7)
      AND voucher.VoucherDate >= DATEADD(day, -30, CONVERT(date, SYSDATETIMEOFFSET() AT TIME ZONE 'SE Asia Standard Time'))
),
InboundValid AS
(
    SELECT CAST(DATEDIFF_BIG(second, ArrivalAt, CompletedAt) / 3600.0 AS decimal(18,4)) AS TotalHours
    FROM InboundBase
    WHERE MissingMilestone = 0 AND InvalidMilestoneOrder = 0
),
InboundPercentiles AS
(
    SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY TotalHours) OVER () AS MedianHours,
           PERCENTILE_CONT(0.9) WITHIN GROUP (ORDER BY TotalHours) OVER () AS P90Hours,
           PERCENTILE_CONT(0.95) WITHIN GROUP (ORDER BY TotalHours) OVER () AS P95Hours
    FROM InboundValid
)
SELECT 'STAT05_DOCK_TO_STOCK_MILESTONES' AS IssueCode,
       COUNT_BIG(*) AS PostedInboundCount,
       COALESCE(SUM(CASE WHEN MissingMilestone = 0 AND InvalidMilestoneOrder = 0 THEN 1 ELSE 0 END), 0) AS ValidDurationSampleCount,
       COALESCE(SUM(MissingMilestone), 0) AS MissingMilestoneCount,
       COALESCE(SUM(InvalidMilestoneOrder), 0) AS InvalidMilestoneOrderCount,
       CAST((SELECT MAX(MedianHours) FROM InboundPercentiles) AS decimal(18,2)) AS MedianTotalHours,
       CAST((SELECT MAX(P90Hours) FROM InboundPercentiles) AS decimal(18,2)) AS P90TotalHours,
       CAST((SELECT MAX(P95Hours) FROM InboundPercentiles) AS decimal(18,2)) AS P95TotalHours
FROM InboundBase;

;WITH OutboundBase AS
(
    SELECT voucher.VoucherId,
           voucher.CreatedAt,
           voucher.PackedAt,
           voucher.ShippedAt,
           CASE WHEN voucher.PackedAt IS NOT NULL
                     AND voucher.ShippedAt IS NOT NULL
                     AND voucher.CreatedAt <= voucher.PackedAt
                     AND voucher.PackedAt <= voucher.ShippedAt THEN 1 ELSE 0 END AS ValidCycle,
           CASE WHEN voucher.PackedAt IS NOT NULL AND voucher.PackedAt < voucher.CreatedAt
                     OR voucher.ShippedAt IS NOT NULL AND voucher.PackedAt IS NULL
                     OR voucher.ShippedAt IS NOT NULL AND voucher.PackedAt IS NOT NULL AND voucher.ShippedAt < voucher.PackedAt
                THEN 1 ELSE 0 END AS InvalidMilestoneOrder
    FROM Vouchers AS voucher
    WHERE voucher.IsCancelled = 0
      AND voucher.VoucherType IN (2, 3, 8)
      AND voucher.CreatedAt >= DATEADD(day, -30, CONVERT(datetime2, SYSDATETIMEOFFSET() AT TIME ZONE 'SE Asia Standard Time'))
),
OutboundValid AS
(
    SELECT CAST(DATEDIFF_BIG(second, CreatedAt, ShippedAt) / 3600.0 AS decimal(18,4)) AS TotalHours
    FROM OutboundBase
    WHERE ValidCycle = 1
),
OutboundPercentiles AS
(
    SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY TotalHours) OVER () AS MedianHours,
           PERCENTILE_CONT(0.9) WITHIN GROUP (ORDER BY TotalHours) OVER () AS P90Hours,
           PERCENTILE_CONT(0.95) WITHIN GROUP (ORDER BY TotalHours) OVER () AS P95Hours
    FROM OutboundValid
)
SELECT 'STAT06_OUTBOUND_MILESTONES' AS IssueCode,
       COUNT_BIG(*) AS OutboundOrderCount,
       COALESCE(SUM(ValidCycle), 0) AS ValidDurationSampleCount,
       COALESCE(SUM(CASE WHEN PackedAt IS NULL THEN 1 ELSE 0 END), 0) AS NotPackedCount,
       COALESCE(SUM(CASE WHEN ShippedAt IS NULL THEN 1 ELSE 0 END), 0) AS NotShippedCount,
       COALESCE(SUM(InvalidMilestoneOrder), 0) AS InvalidMilestoneOrderCount,
       CAST((SELECT MAX(MedianHours) FROM OutboundPercentiles) AS decimal(18,2)) AS MedianTotalHours,
       CAST((SELECT MAX(P90Hours) FROM OutboundPercentiles) AS decimal(18,2)) AS P90TotalHours,
       CAST((SELECT MAX(P95Hours) FROM OutboundPercentiles) AS decimal(18,2)) AS P95TotalHours
FROM OutboundBase;

;WITH EligibleLines AS
(
    SELECT detail.VoucherDetailId,
           detail.BaseQty
    FROM VoucherDetails AS detail
    INNER JOIN Vouchers AS voucher ON voucher.VoucherId = detail.VoucherId
    WHERE voucher.IsCancelled = 0
      AND voucher.IsPosted = 1
      AND voucher.VoucherType IN (2, 3, 8)
      AND detail.BaseQty > 0.0001
),
IssuedByLine AS
(
    SELECT transactionRow.VoucherDetailId,
           -SUM(transactionRow.QuantityDelta) AS IssuedBaseQty
    FROM InventoryTransactions AS transactionRow
    WHERE transactionRow.VoucherDetailId IS NOT NULL
      AND transactionRow.TransactionType = 7
      AND transactionRow.QuantityDelta < 0
    GROUP BY transactionRow.VoucherDetailId
),
LineOutcome AS
(
    SELECT eligible.VoucherDetailId,
           eligible.BaseQty,
           COALESCE(issued.IssuedBaseQty, 0) AS IssuedBaseQty
    FROM EligibleLines AS eligible
    LEFT JOIN IssuedByLine AS issued ON issued.VoucherDetailId = eligible.VoucherDetailId
)
SELECT 'STAT07_OUTBOUND_LINE_FILL' AS IssueCode,
       COUNT_BIG(*) AS EligiblePostedLines,
       COALESCE(SUM(CASE WHEN ABS(IssuedBaseQty - BaseQty) <= 0.0001 THEN 1 ELSE 0 END), 0) AS FullyFulfilledLines,
       COALESCE(SUM(CASE WHEN IssuedBaseQty < BaseQty - 0.0001 THEN 1 ELSE 0 END), 0) AS ShortLines,
       COALESCE(SUM(CASE WHEN IssuedBaseQty > BaseQty + 0.0001 THEN 1 ELSE 0 END), 0) AS OverIssuedLines,
       COALESCE(SUM(CASE WHEN IssuedBaseQty <= 0.0001 THEN 1 ELSE 0 END), 0) AS MissingIssueLedgerLines,
       CAST(CASE WHEN COUNT_BIG(*) = 0 THEN NULL
            ELSE 100.0 * SUM(CASE WHEN ABS(IssuedBaseQty - BaseQty) <= 0.0001 THEN 1 ELSE 0 END) / COUNT_BIG(*)
       END AS decimal(9,4)) AS LineFillRatePercent
FROM LineOutcome;
