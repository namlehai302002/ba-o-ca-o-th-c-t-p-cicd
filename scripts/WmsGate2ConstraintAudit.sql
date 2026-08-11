/*
Gate 2 read-only schema contract audit for core WMS tables.
Each result set returns missing or unsafe metadata only.
*/

SET NOCOUNT ON;

;WITH RequiredRelation(TableName, ColumnName, ReferencedTableName) AS
(
    SELECT * FROM (VALUES
        ('AppUsers', 'RoleId', 'AppRoles'),
        ('ItemLocations', 'ItemId', 'Items'),
        ('ItemLocations', 'LocationId', 'Locations'),
        ('VoucherDetails', 'VoucherId', 'Vouchers'),
        ('VoucherDetails', 'ItemId', 'Items'),
        ('VoucherDetails', 'TransactionUomId', 'UnitsOfMeasure'),
        ('StockReservations', 'VoucherId', 'Vouchers'),
        ('StockReservations', 'ItemId', 'Items'),
        ('StockReservations', 'LocationId', 'Locations'),
        ('InventoryTransactions', 'WarehouseId', 'Warehouses'),
        ('InventoryTransactions', 'ItemId', 'Items'),
        ('InventoryTransactions', 'LocationId', 'Locations')
    ) value(TableName, ColumnName, ReferencedTableName)
)
SELECT 'REQUIRED_FOREIGN_KEY_MISSING_OR_UNTRUSTED' AS IssueCode,
       required.TableName, required.ColumnName, required.ReferencedTableName
FROM RequiredRelation required
WHERE NOT EXISTS
(
    SELECT 1
    FROM sys.foreign_keys fk
    JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
    JOIN sys.tables parentTable ON parentTable.object_id = fk.parent_object_id
    JOIN sys.columns parentColumn ON parentColumn.object_id = parentTable.object_id
                                  AND parentColumn.column_id = fkc.parent_column_id
    JOIN sys.tables referencedTable ON referencedTable.object_id = fk.referenced_object_id
    WHERE parentTable.name = required.TableName
      AND parentColumn.name = required.ColumnName
      AND referencedTable.name = required.ReferencedTableName
      AND fk.is_disabled = 0
      AND fk.is_not_trusted = 0
);

;WITH RequiredColumn(TableName, ColumnName) AS
(
    SELECT * FROM (VALUES
        ('AppUsers', 'UserName'), ('AppUsers', 'PasswordHash'), ('AppUsers', 'RoleId'), ('AppUsers', 'IsActive'),
        ('Items', 'ItemCode'), ('Items', 'ItemName'), ('Items', 'BaseUomId'), ('Items', 'IsActive'),
        ('ItemLocations', 'ItemId'), ('ItemLocations', 'LocationId'), ('ItemLocations', 'Quantity'), ('ItemLocations', 'ReservedQty'), ('ItemLocations', 'HoldStatus'),
        ('Vouchers', 'VoucherCode'), ('Vouchers', 'VoucherType'), ('Vouchers', 'WarehouseId'), ('Vouchers', 'CreatedBy'),
        ('VoucherDetails', 'VoucherId'), ('VoucherDetails', 'ItemId'), ('VoucherDetails', 'TransactionQty'), ('VoucherDetails', 'TransactionUomId'), ('VoucherDetails', 'ConversionRate'), ('VoucherDetails', 'BaseQty'),
        ('StockReservations', 'VoucherId'), ('StockReservations', 'ItemId'), ('StockReservations', 'LocationId'), ('StockReservations', 'ReservedQty'), ('StockReservations', 'ConsumedQty'), ('StockReservations', 'ReleasedQty'),
        ('InventoryTransactions', 'TransactionType'), ('InventoryTransactions', 'IdempotencyKey'), ('InventoryTransactions', 'WarehouseId'), ('InventoryTransactions', 'ItemId'), ('InventoryTransactions', 'LocationId'), ('InventoryTransactions', 'QuantityDelta')
    ) value(TableName, ColumnName)
)
SELECT 'REQUIRED_COLUMN_NULLABLE_OR_MISSING' AS IssueCode,
       required.TableName, required.ColumnName
FROM RequiredColumn required
LEFT JOIN sys.tables tableMetadata ON tableMetadata.name = required.TableName
LEFT JOIN sys.columns columnMetadata ON columnMetadata.object_id = tableMetadata.object_id
                                    AND columnMetadata.name = required.ColumnName
WHERE columnMetadata.column_id IS NULL OR columnMetadata.is_nullable = 1;

;WITH RequiredIndex(TableName, ColumnList) AS
(
    SELECT * FROM (VALUES
        ('ItemCategories', 'CategoryCode'),
        ('UnitsOfMeasure', 'UomCode'),
        ('Items', 'ItemCode'),
        ('Warehouses', 'WarehouseCode'),
        ('Partners', 'PartnerCode'),
        ('AppUsers', 'UserName'),
        ('Vouchers', 'WarehouseId,VoucherCode'),
        ('UnitConversions', 'ItemId,FromUomId,ToUomId'),
        ('UnitConversions', 'FromUomId,ToUomId'),
        ('ItemLocations', 'OwnerPartnerId,ItemId,LocationId,HoldStatus'),
        ('ItemLocations', 'OwnerPartnerId,ItemId,LocationId,LotNumber,HoldStatus'),
        ('ItemLocations', 'OwnerPartnerId,ItemId,LocationId,ExpiryDate,HoldStatus'),
        ('ItemLocations', 'OwnerPartnerId,ItemId,LocationId,LotNumber,ExpiryDate,HoldStatus'),
        ('InventoryTransactions', 'IdempotencyKey')
    ) value(TableName, ColumnList)
),
ActualUniqueIndex AS
(
    SELECT tableMetadata.name AS TableName,
           STRING_AGG(columnMetadata.name, ',') WITHIN GROUP (ORDER BY indexColumn.key_ordinal) AS ColumnList
    FROM sys.indexes indexMetadata
    JOIN sys.tables tableMetadata ON tableMetadata.object_id = indexMetadata.object_id
    JOIN sys.index_columns indexColumn ON indexColumn.object_id = indexMetadata.object_id
                                      AND indexColumn.index_id = indexMetadata.index_id
                                      AND indexColumn.key_ordinal > 0
    JOIN sys.columns columnMetadata ON columnMetadata.object_id = indexColumn.object_id
                                   AND columnMetadata.column_id = indexColumn.column_id
    WHERE indexMetadata.is_unique = 1
      AND indexMetadata.is_disabled = 0
    GROUP BY tableMetadata.name, indexMetadata.index_id
)
SELECT 'REQUIRED_UNIQUE_INDEX_MISSING_OR_DISABLED' AS IssueCode,
       required.TableName, required.ColumnList
FROM RequiredIndex required
WHERE NOT EXISTS
(
    SELECT 1
    FROM ActualUniqueIndex actual
    WHERE actual.TableName = required.TableName
      AND actual.ColumnList = required.ColumnList
);

;WITH RequiredCheck(ConstraintName) AS
(
    SELECT * FROM (VALUES
        ('CK_ItemLocations_Qty_NonNegative'),
        ('CK_StockReservations_Qty_NonNegative'),
        ('CK_StockReservations_Qty_ClosedWithinReserved'),
        ('CK_VoucherDetails_DefectQty_NonNegative'),
        ('CK_VoucherDetails_ExpiryAfterManufacturing')
    ) value(ConstraintName)
)
SELECT 'REQUIRED_CHECK_CONSTRAINT_MISSING_OR_UNTRUSTED' AS IssueCode, required.ConstraintName
FROM RequiredCheck required
WHERE NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints checkMetadata
    WHERE checkMetadata.name = required.ConstraintName
      AND checkMetadata.is_disabled = 0
      AND checkMetadata.is_not_trusted = 0
);

;WITH DecimalContract(TableName, ColumnName, ExpectedPrecision, ExpectedScale) AS
(
    SELECT * FROM (VALUES
        ('ItemLocations', 'Quantity', 18, 4), ('ItemLocations', 'ReservedQty', 18, 4),
        ('StockReservations', 'ReservedQty', 18, 4), ('StockReservations', 'ConsumedQty', 18, 4), ('StockReservations', 'ReleasedQty', 18, 4),
        ('VoucherDetails', 'TransactionQty', 18, 4), ('VoucherDetails', 'ConversionRate', 18, 6), ('VoucherDetails', 'BaseQty', 18, 4), ('VoucherDetails', 'UnitPrice', 18, 4), ('VoucherDetails', 'LineAmount', 18, 4),
        ('Vouchers', 'TotalAmount', 18, 4),
        ('UnitConversions', 'ConversionRate', 18, 6),
        ('InventoryTransactions', 'QuantityDelta', 18, 4), ('InventoryTransactions', 'ReservedDelta', 18, 4), ('InventoryTransactions', 'AvailableDelta', 18, 4),
        ('InventoryTransactions', 'QuantityBefore', 18, 4), ('InventoryTransactions', 'QuantityAfter', 18, 4),
        ('InventoryTransactions', 'ReservedBefore', 18, 4), ('InventoryTransactions', 'ReservedAfter', 18, 4),
        ('InventoryTransactions', 'AvailableBefore', 18, 4), ('InventoryTransactions', 'AvailableAfter', 18, 4)
    ) value(TableName, ColumnName, ExpectedPrecision, ExpectedScale)
)
SELECT 'DECIMAL_PRECISION_SCALE_MISMATCH' AS IssueCode,
       contract.TableName, contract.ColumnName,
       typeMetadata.name AS ActualType, columnMetadata.precision AS ActualPrecision, columnMetadata.scale AS ActualScale,
       contract.ExpectedPrecision, contract.ExpectedScale
FROM DecimalContract contract
LEFT JOIN sys.tables tableMetadata ON tableMetadata.name = contract.TableName
LEFT JOIN sys.columns columnMetadata ON columnMetadata.object_id = tableMetadata.object_id
                                    AND columnMetadata.name = contract.ColumnName
LEFT JOIN sys.types typeMetadata ON typeMetadata.user_type_id = columnMetadata.user_type_id
WHERE columnMetadata.column_id IS NULL
   OR typeMetadata.name NOT IN ('decimal', 'numeric')
   OR columnMetadata.precision <> contract.ExpectedPrecision
   OR columnMetadata.scale <> contract.ExpectedScale;

SELECT 'CORE_FOREIGN_KEY_CASCADE_DELETE' AS IssueCode,
       parentTable.name AS ParentTable, fk.name AS ForeignKeyName,
       referencedTable.name AS ReferencedTable, fk.delete_referential_action_desc AS DeleteAction
FROM sys.foreign_keys fk
JOIN sys.tables parentTable ON parentTable.object_id = fk.parent_object_id
JOIN sys.tables referencedTable ON referencedTable.object_id = fk.referenced_object_id
WHERE parentTable.name IN ('ItemLocations', 'VoucherDetails', 'StockReservations', 'InventoryTransactions')
  AND fk.delete_referential_action_desc = 'CASCADE'
  AND NOT (parentTable.name = 'VoucherDetails' AND referencedTable.name = 'Vouchers');

SELECT 'FLOATING_POINT_USED_FOR_QUANTITY_OR_MONEY' AS IssueCode,
       tableMetadata.name AS TableName, columnMetadata.name AS ColumnName, typeMetadata.name AS TypeName
FROM sys.tables tableMetadata
JOIN sys.columns columnMetadata ON columnMetadata.object_id = tableMetadata.object_id
JOIN sys.types typeMetadata ON typeMetadata.user_type_id = columnMetadata.user_type_id
WHERE typeMetadata.name IN ('float', 'real')
  AND (columnMetadata.name LIKE '%Qty%'
    OR columnMetadata.name LIKE '%Quantity%'
    OR columnMetadata.name LIKE '%Amount%'
    OR columnMetadata.name LIKE '%Price%'
    OR columnMetadata.name LIKE '%Rate%'
    OR columnMetadata.name LIKE '%Cost%');
