SET NOCOUNT ON;

SELECT 'OPEN_INBOUND_UOM_QTY_MISMATCH' AS IssueCode,
       vd.VoucherDetailId,
       vd.VoucherId,
       vd.ItemId
FROM VoucherDetails vd
JOIN Vouchers v ON v.VoucherId = vd.VoucherId
WHERE v.VoucherType IN (1, 4, 7)
  AND v.IsPosted = 0
  AND v.IsCancelled = 0
  AND
  (
      vd.TransactionQty <= 0
      OR vd.ConversionRate <= 0
      OR vd.BaseQty <= 0
      OR ABS(vd.BaseQty - ROUND(vd.TransactionQty * vd.ConversionRate, 4)) > 0.0001
  );

SELECT 'ALL_INBOUND_UOM_QTY_MISMATCH' AS IssueCode,
       vd.VoucherDetailId,
       vd.VoucherId,
       vd.ItemId
FROM VoucherDetails vd
JOIN Vouchers v ON v.VoucherId = vd.VoucherId
WHERE v.VoucherType IN (1, 4, 7)
  AND
  (
      vd.TransactionQty <= 0
      OR vd.ConversionRate <= 0
      OR vd.BaseQty <= 0
      OR ABS(vd.BaseQty - ROUND(vd.TransactionQty * vd.ConversionRate, 4)) > 0.0001
  );

SELECT 'ACTIVE_ITEM_BASE_UOM_INVALID' AS IssueCode,
       i.ItemId,
       i.BaseUomId
FROM Items i
LEFT JOIN UnitsOfMeasure u ON u.UomId = i.BaseUomId
WHERE i.IsActive = 1
  AND (u.UomId IS NULL OR u.IsActive = 0);

SELECT 'BACKORDER_UOM_QTY_MISMATCH' AS IssueCode,
       vd.VoucherDetailId,
       vd.VoucherId,
       vd.ItemId
FROM VoucherDetails vd
JOIN Vouchers v ON v.VoucherId = vd.VoucherId
WHERE v.ParentVoucherId IS NOT NULL
  AND
  (
      vd.TransactionQty <= 0
      OR vd.ConversionRate <= 0
      OR vd.BaseQty <= 0
      OR ABS(vd.BaseQty - ROUND(vd.TransactionQty * vd.ConversionRate, 4)) > 0.0001
  );
