SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT 'AI_DQ_01_SHEET_COVERAGE' AS IssueCode,
       COUNT_BIG(*) AS TotalSheets,
       SUM(CASE WHEN Status = 1 THEN 1 ELSE 0 END) AS DraftSheets,
       SUM(CASE WHEN Status = 2 THEN 1 ELSE 0 END) AS CountingSheets,
       SUM(CASE WHEN Status = 3 THEN 1 ELSE 0 END) AS CountedSheets,
       SUM(CASE WHEN Status = 4 THEN 1 ELSE 0 END) AS ApprovedSheets,
       SUM(CASE WHEN Status = 4 AND ApprovedAt IS NULL THEN 1 ELSE 0 END) AS ApprovedWithoutTimestamp,
       MIN(CASE WHEN Status = 4 THEN ApprovedAt END) AS FirstApprovedAt,
       MAX(CASE WHEN Status = 4 THEN ApprovedAt END) AS LastApprovedAt
FROM StockCountSheets;

SELECT 'AI_DQ_02_LABEL_COVERAGE' AS IssueCode,
       COUNT_BIG(*) AS ApprovedLines,
       SUM(CASE WHEN line.CountedQty IS NULL THEN 1 ELSE 0 END) AS MissingCountedQty,
       SUM(CASE WHEN line.Variance IS NULL THEN 1 ELSE 0 END) AS MissingVariance,
       SUM(CASE WHEN ABS(COALESCE(line.CountedQty, 0) - line.SystemQty) <= 0.0001 THEN 1 ELSE 0 END) AS ExactLines,
       SUM(CASE WHEN ABS(COALESCE(line.CountedQty, 0) - line.SystemQty) > 0.0001 THEN 1 ELSE 0 END) AS VarianceLines,
       SUM(ABS(COALESCE(line.CountedQty, 0) - line.SystemQty)) AS AbsoluteVarianceQty
FROM StockCountLines AS line
INNER JOIN StockCountSheets AS sheet ON sheet.StockCountSheetId = line.StockCountSheetId
WHERE sheet.Status = 4;

SELECT 'AI_DQ_03_SCOPE_AND_TRACKING' AS IssueCode,
       COUNT_BIG(*) AS ApprovedLines,
       SUM(CASE WHEN line.OwnerPartnerId IS NULL THEN 1 ELSE 0 END) AS LinesWithoutOwner,
       SUM(CASE WHEN item.TrackLot = 1 AND NULLIF(LTRIM(RTRIM(line.LotNumber)), '') IS NULL THEN 1 ELSE 0 END) AS TrackedLotMissing,
       SUM(CASE WHEN item.TrackExpiry = 1 AND line.ExpiryDate IS NULL THEN 1 ELSE 0 END) AS TrackedExpiryMissing,
       SUM(CASE WHEN zone.WarehouseId <> sheet.WarehouseId THEN 1 ELSE 0 END) AS WarehouseLocationMismatch
FROM StockCountLines AS line
INNER JOIN StockCountSheets AS sheet ON sheet.StockCountSheetId = line.StockCountSheetId
INNER JOIN Items AS item ON item.ItemId = line.ItemId
INNER JOIN Locations AS location ON location.LocationId = line.LocationId
INNER JOIN Zones AS zone ON zone.ZoneId = location.ZoneId
WHERE sheet.Status = 4;

SELECT 'AI_DQ_04_SCHEDULE_MILESTONE' AS IssueCode,
       COUNT_BIG(*) AS ActiveSchedules,
       COALESCE(SUM(CASE WHEN schedule.LastCountedAt IS NOT NULL THEN 1 ELSE 0 END), 0) AS SchedulesWithLastCountedAt,
       COALESCE(SUM(CASE WHEN schedule.LastCountedAt IS NOT NULL AND approved.LatestApprovedAt IS NULL THEN 1 ELSE 0 END), 0) AS LastCountedWithoutApprovedEvidence,
       COALESCE(SUM(CASE WHEN approved.LatestApprovedAt IS NOT NULL AND schedule.LastCountedAt > approved.LatestApprovedAt THEN 1 ELSE 0 END), 0) AS LastCountedAfterLatestApproval,
       COALESCE(SUM(CASE WHEN approved.LatestApprovedAt IS NOT NULL AND (schedule.LastCountedAt IS NULL OR schedule.LastCountedAt < approved.LatestApprovedAt) THEN 1 ELSE 0 END), 0) AS ScheduleBehindLatestApproval
FROM CycleCountSchedules AS schedule
INNER JOIN CycleCountPrograms AS program ON program.ProgramId = schedule.ProgramId
OUTER APPLY
(
    SELECT MAX(sheet.ApprovedAt) AS LatestApprovedAt
    FROM StockCountLines AS line
    INNER JOIN StockCountSheets AS sheet ON sheet.StockCountSheetId = line.StockCountSheetId
    WHERE sheet.Status = 4
      AND sheet.WarehouseId = program.WarehouseId
      AND line.ItemId = schedule.ItemId
      AND (line.OwnerPartnerId = schedule.OwnerPartnerId OR (line.OwnerPartnerId IS NULL AND schedule.OwnerPartnerId IS NULL))
      AND line.LocationId = schedule.LocationId
) AS approved
WHERE schedule.IsActive = 1 AND program.IsActive = 1;

SELECT 'AI_DQ_05_REASON_COMPLETENESS' AS IssueCode,
       COUNT_BIG(*) AS ApprovedSheetsWithVariance,
       COALESCE(SUM(CASE WHEN NULLIF(LTRIM(RTRIM(sheet.ApprovalReason)), '') IS NULL THEN 1 ELSE 0 END), 0) AS MissingApprovalReason,
       CAST(0 AS int) AS StructuredLineReasonSupported
FROM StockCountSheets AS sheet
WHERE sheet.Status = 4
  AND EXISTS
  (
      SELECT 1
      FROM StockCountLines AS line
      WHERE line.StockCountSheetId = sheet.StockCountSheetId
        AND ABS(COALESCE(line.CountedQty, 0) - line.SystemQty) > 0.0001
  );

SELECT 'AI_DQ_06_GRAIN_PROFILE' AS IssueCode,
       COUNT_BIG(*) AS ApprovedLines,
       COUNT(DISTINCT CAST(sheet.WarehouseId AS varchar(20)) + '|'
           + COALESCE(CAST(line.OwnerPartnerId AS varchar(20)), '~') + '|'
           + CAST(line.ItemId AS varchar(20)) + '|'
           + CAST(line.LocationId AS varchar(20))) AS ScheduleGrainKeys,
       COUNT(DISTINCT CAST(sheet.WarehouseId AS varchar(20)) + '|'
           + COALESCE(CAST(line.OwnerPartnerId AS varchar(20)), '~') + '|'
           + CAST(line.ItemId AS varchar(20)) + '|'
           + CAST(line.LocationId AS varchar(20)) + '|'
           + COALESCE(line.LotNumber, '~') + '|'
           + COALESCE(CONVERT(varchar(10), line.ExpiryDate, 23), '~')) AS PredictionGrainKeys,
       SUM(CASE WHEN line.LotNumber IS NOT NULL OR line.ExpiryDate IS NOT NULL THEN 1 ELSE 0 END) AS LotOrExpiryLines
FROM StockCountLines AS line
INNER JOIN StockCountSheets AS sheet ON sheet.StockCountSheetId = line.StockCountSheetId
WHERE sheet.Status = 4;

SELECT 'AI_DQ_07_TEMPORAL_DEPTH' AS IssueCode,
       COUNT_BIG(*) AS ApprovedSheets,
       COUNT(DISTINCT CONVERT(date, ApprovedAt)) AS ApprovedDays,
       DATEDIFF(day, MIN(ApprovedAt), MAX(ApprovedAt)) AS HistorySpanDays,
       MIN(ApprovedAt) AS FirstApprovedAt,
       MAX(ApprovedAt) AS LastApprovedAt
FROM StockCountSheets
WHERE Status = 4 AND ApprovedAt IS NOT NULL;

SELECT 'AI_DQ_08_MULTI_OWNER_SHEETS' AS IssueCode,
       COUNT_BIG(*) AS ApprovedSheetsWithMultipleOwners
FROM StockCountSheets AS sheet
WHERE sheet.Status = 4
  AND 1 <
  (
      SELECT COUNT(DISTINCT COALESCE(line.OwnerPartnerId, -1))
      FROM StockCountLines AS line
      WHERE line.StockCountSheetId = sheet.StockCountSheetId
  );
