using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WMS.Data;

#nullable disable

namespace WMS.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260705070000_RepairReportFefoDatabaseGuards")]
    public partial class RepairReportFefoDatabaseGuards : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(CreateIndexIfMissing(
                tableName: "InventoryTransactions",
                indexName: "UX_InventoryTransactions_IdempotencyKey",
                columns: "[IdempotencyKey]",
                unique: true));

            migrationBuilder.Sql(CreateIndexIfMissing(
                tableName: "InventoryTransactions",
                indexName: "IX_InventoryTransactions_Owner_Warehouse_Date",
                columns: "[OwnerPartnerId], [WarehouseId], [TransactionAt]"));

            migrationBuilder.Sql(CreateIndexIfMissing(
                tableName: "InventoryTransactions",
                indexName: "IX_InventoryTransactions_Warehouse_Date",
                columns: "[WarehouseId], [TransactionAt]"));

            migrationBuilder.Sql(CreateIndexIfMissing(
                tableName: "InventoryTransactions",
                indexName: "IX_InventoryTransactions_Item_Location_Date",
                columns: "[ItemId], [LocationId], [TransactionAt]"));

            migrationBuilder.Sql(CreateIndexIfMissing(
                tableName: "InventoryTransactions",
                indexName: "IX_InventoryTransactions_Type_Date",
                columns: "[TransactionType], [TransactionAt]"));

            migrationBuilder.Sql(CreateIndexIfMissing(
                tableName: "InventoryTransactions",
                indexName: "IX_InventoryTransactions_Reference",
                columns: "[ReferenceType], [ReferenceId]"));

            migrationBuilder.Sql(CreateIndexIfMissing(
                tableName: "InventoryTransactions",
                indexName: "IX_InventoryTransactions_GroupKey",
                columns: "[TransactionGroupKey]"));

            migrationBuilder.Sql(CreateIndexIfMissing(
                tableName: "InventoryTransactions",
                indexName: "IX_InventoryTransactions_LicensePlateId",
                columns: "[LicensePlateId]"));

            migrationBuilder.Sql(CreateIndexIfMissing(
                tableName: "InventoryTransactions",
                indexName: "IX_InventoryTransactions_SerialNumberId",
                columns: "[SerialNumberId]"));

            migrationBuilder.Sql(CreateIndexIfMissing(
                tableName: "RequestTelemetryLogs",
                indexName: "IX_RequestTelemetryLogs_Time_Path_Status",
                columns: "[CreatedAt], [Path], [StatusCode]"));

            migrationBuilder.Sql(CreateIndexIfMissing(
                tableName: "RequestTelemetryLogs",
                indexName: "IX_RequestTelemetryLogs_CorrelationId",
                columns: "[CorrelationId]"));

            migrationBuilder.Sql(CreateIndexIfMissing(
                tableName: "AuditLogs",
                indexName: "IX_AuditLogs_Table_Date",
                columns: "[TableName], [ChangedAt]"));

            migrationBuilder.Sql(CreateIndexIfMissing(
                tableName: "ItemLocations",
                indexName: "IX_ItemLocations_Item_Location_Hold_NoBatch",
                columns: "[OwnerPartnerId], [ItemId], [LocationId], [HoldStatus]",
                unique: true,
                filter: "[LotNumber] IS NULL AND [ExpiryDate] IS NULL"));

            migrationBuilder.Sql(CreateIndexIfMissing(
                tableName: "ItemLocations",
                indexName: "IX_ItemLocations_Item_Location_Lot_Hold",
                columns: "[OwnerPartnerId], [ItemId], [LocationId], [LotNumber], [HoldStatus]",
                unique: true,
                filter: "[LotNumber] IS NOT NULL AND [ExpiryDate] IS NULL"));

            migrationBuilder.Sql(CreateIndexIfMissing(
                tableName: "ItemLocations",
                indexName: "IX_ItemLocations_Item_Location_Expiry_Hold",
                columns: "[OwnerPartnerId], [ItemId], [LocationId], [ExpiryDate], [HoldStatus]",
                unique: true,
                filter: "[LotNumber] IS NULL AND [ExpiryDate] IS NOT NULL"));

            migrationBuilder.Sql(CreateIndexIfMissing(
                tableName: "ItemLocations",
                indexName: "IX_ItemLocations_Item_Location_Lot_Expiry_Hold",
                columns: "[OwnerPartnerId], [ItemId], [LocationId], [LotNumber], [ExpiryDate], [HoldStatus]",
                unique: true,
                filter: "[LotNumber] IS NOT NULL AND [ExpiryDate] IS NOT NULL"));

            migrationBuilder.Sql(CreateIndexIfMissing(
                tableName: "ItemLocations",
                indexName: "IX_ItemLocations_Location_Qty",
                columns: "[LocationId], [Quantity]"));

            migrationBuilder.Sql(CreateIndexIfMissing(
                tableName: "ItemLocations",
                indexName: "IX_ItemLocations_Owner_SnapshotKey",
                columns: "[OwnerPartnerId], [ItemId], [LocationId], [HoldStatus], [LotNumber], [ExpiryDate]"));

            migrationBuilder.Sql(AddCheckConstraintIfMissing(
                tableName: "ItemLocations",
                constraintName: "CK_ItemLocations_Qty_NonNegative",
                predicate: "[Quantity] >= 0 AND [ReservedQty] >= 0 AND [Quantity] >= [ReservedQty]"));

            migrationBuilder.Sql(CreateIndexIfMissing(
                tableName: "VoucherDetails",
                indexName: "IX_VoucherDetails_Voucher_Item",
                columns: "[VoucherId], [ItemId]"));

            migrationBuilder.Sql(CreateIndexIfMissing(
                tableName: "VoucherDetails",
                indexName: "IX_VoucherDetails_Owner_Item",
                columns: "[OwnerPartnerId], [ItemId]"));

            migrationBuilder.Sql(CreateIndexIfMissing(
                tableName: "VoucherDetails",
                indexName: "IX_VoucherDetails_Item_Lot_Expiry",
                columns: "[ItemId], [LotNumber], [ExpiryDate]"));

            migrationBuilder.Sql(AddCheckConstraintIfMissing(
                tableName: "VoucherDetails",
                constraintName: "CK_VoucherDetails_DefectQty_NonNegative",
                predicate: "[DefectQty] >= 0 AND [DefectBaseQty] >= 0"));

            migrationBuilder.Sql(AddCheckConstraintIfMissing(
                tableName: "VoucherDetails",
                constraintName: "CK_VoucherDetails_ExpiryAfterManufacturing",
                predicate: "[ManufacturingDate] IS NULL OR [ExpiryDate] IS NULL OR [ExpiryDate] >= [ManufacturingDate]"));

            migrationBuilder.Sql(CreateIndexIfMissing(
                tableName: "Items",
                indexName: "IX_Items_ItemCode",
                columns: "[ItemCode]",
                unique: true));

            migrationBuilder.Sql(CreateIndexIfMissing(
                tableName: "Vouchers",
                indexName: "IX_Vouchers_WarehouseId_VoucherCode",
                columns: "[WarehouseId], [VoucherCode]",
                unique: true));

            migrationBuilder.Sql(CreateIndexIfMissing(
                tableName: "StockSnapshots",
                indexName: "IX_StockSnapshots_Date_Item_Warehouse_Owner",
                columns: "[SnapshotDate], [ItemId], [WarehouseId], [OwnerPartnerId]"));

            migrationBuilder.Sql(CreateIndexIfMissing(
                tableName: "StockSnapshots",
                indexName: "IX_StockSnapshots_StockSnapshotRunId",
                columns: "[StockSnapshotRunId]"));

            migrationBuilder.Sql(CreateIndexIfMissing(
                tableName: "StockSnapshots",
                indexName: "IX_StockSnapshots_StockSnapshotRunId_ItemId_OwnerPartnerId",
                columns: "[StockSnapshotRunId], [ItemId], [OwnerPartnerId]",
                unique: true));

            migrationBuilder.Sql(CreateIndexIfMissing(
                tableName: "StockSnapshotRuns",
                indexName: "IX_StockSnapshotRuns_Warehouse_Date_CreatedAt",
                columns: "[WarehouseId], [SnapshotDate], [CreatedAt]"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // This repair migration is intentionally non-destructive on rollback.
        }

        private static string CreateIndexIfMissing(
            string tableName,
            string indexName,
            string columns,
            bool unique = false,
            string filter = null)
        {
            var uniqueSql = unique ? "UNIQUE " : string.Empty;
            var filterSql = string.IsNullOrWhiteSpace(filter) ? string.Empty : $" WHERE {filter}";

            return $@"
DECLARE @schema_{indexName} sysname;
SELECT TOP (1) @schema_{indexName} = s.name
FROM sys.tables t
INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE t.name = N'{tableName}'
ORDER BY CASE WHEN s.name = SCHEMA_NAME() THEN 0 WHEN s.name = N'dbo' THEN 1 ELSE 2 END;

IF @schema_{indexName} IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes i
       WHERE i.object_id = OBJECT_ID(QUOTENAME(@schema_{indexName}) + N'.[{tableName}]')
         AND i.name = N'{indexName}')
BEGIN
    DECLARE @sql_{indexName} nvarchar(max) =
        N'CREATE {uniqueSql}INDEX [{indexName}] ON ' + QUOTENAME(@schema_{indexName}) + N'.[{tableName}] ({columns}){filterSql}';
    EXEC sys.sp_executesql @sql_{indexName};
END";
        }

        private static string AddCheckConstraintIfMissing(
            string tableName,
            string constraintName,
            string predicate)
        {
            return $@"
DECLARE @schema_{constraintName} sysname;
SELECT TOP (1) @schema_{constraintName} = s.name
FROM sys.tables t
INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE t.name = N'{tableName}'
ORDER BY CASE WHEN s.name = SCHEMA_NAME() THEN 0 WHEN s.name = N'dbo' THEN 1 ELSE 2 END;

IF @schema_{constraintName} IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.check_constraints cc
       WHERE cc.parent_object_id = OBJECT_ID(QUOTENAME(@schema_{constraintName}) + N'.[{tableName}]')
         AND cc.name = N'{constraintName}')
BEGIN
    DECLARE @sql_{constraintName} nvarchar(max) =
        N'ALTER TABLE ' + QUOTENAME(@schema_{constraintName}) + N'.[{tableName}] WITH CHECK ADD CONSTRAINT [{constraintName}] CHECK ({predicate});'
        + N' ALTER TABLE ' + QUOTENAME(@schema_{constraintName}) + N'.[{tableName}] CHECK CONSTRAINT [{constraintName}];';
    EXEC sys.sp_executesql @sql_{constraintName};
END";
        }
    }
}
