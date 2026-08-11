using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WMS.Data;

#nullable disable

namespace WMS.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260706132000_P0P3InventoryBusinessHardening_20260706")]
    public partial class P0P3InventoryBusinessHardening_20260706 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DropForeignKeyIfExists("ItemLocations", "FK_ItemLocations_Items_ItemId"));
            migrationBuilder.Sql(DropForeignKeyIfExists("ItemLocations", "FK_ItemLocations_Locations_LocationId"));

            migrationBuilder.Sql(AddForeignKeyIfMissing(
                tableName: "ItemLocations",
                constraintName: "FK_ItemLocations_Items_ItemId",
                columnName: "ItemId",
                principalTableName: "Items",
                principalColumnName: "ItemId"));

            migrationBuilder.Sql(AddForeignKeyIfMissing(
                tableName: "ItemLocations",
                constraintName: "FK_ItemLocations_Locations_LocationId",
                columnName: "LocationId",
                principalTableName: "Locations",
                principalColumnName: "LocationId"));

            migrationBuilder.Sql(DropIndexIfExists(
                tableName: "StockReservations",
                indexName: "IX_StockReservations_VoucherId_VoucherDetailId_ItemId_LocationId_LotNumber_ExpiryDate"));

            migrationBuilder.Sql(CreateIndexIfMissing(
                tableName: "StockReservations",
                indexName: "IX_StockReservations_VoucherId_VoucherDetailId_OwnerPartnerId_ItemId_LocationId_LotNumber_ExpiryDate",
                columns: "[VoucherId], [VoucherDetailId], [OwnerPartnerId], [ItemId], [LocationId], [LotNumber], [ExpiryDate]",
                unique: true,
                filter: "[Status] = 1"));

            migrationBuilder.Sql(AddCheckConstraintIfMissing(
                tableName: "StockReservations",
                constraintName: "CK_StockReservations_Qty_ClosedWithinReserved",
                predicate: "[ConsumedQty] + [ReleasedQty] <= [ReservedQty]"));

            migrationBuilder.Sql(CreateIndexIfMissing(
                tableName: "StockCountLines",
                indexName: "UX_StockCountLines_Sheet_Owner_Item_Location_NoBatch",
                columns: "[StockCountSheetId], [OwnerPartnerId], [ItemId], [LocationId]",
                unique: true,
                filter: "[LotNumber] IS NULL AND [ExpiryDate] IS NULL"));

            migrationBuilder.Sql(CreateIndexIfMissing(
                tableName: "StockCountLines",
                indexName: "UX_StockCountLines_Sheet_Owner_Item_Location_Lot",
                columns: "[StockCountSheetId], [OwnerPartnerId], [ItemId], [LocationId], [LotNumber]",
                unique: true,
                filter: "[LotNumber] IS NOT NULL AND [ExpiryDate] IS NULL"));

            migrationBuilder.Sql(CreateIndexIfMissing(
                tableName: "StockCountLines",
                indexName: "UX_StockCountLines_Sheet_Owner_Item_Location_Expiry",
                columns: "[StockCountSheetId], [OwnerPartnerId], [ItemId], [LocationId], [ExpiryDate]",
                unique: true,
                filter: "[LotNumber] IS NULL AND [ExpiryDate] IS NOT NULL"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DropIndexIfExists(
                tableName: "StockCountLines",
                indexName: "UX_StockCountLines_Sheet_Owner_Item_Location_Expiry"));
            migrationBuilder.Sql(DropIndexIfExists(
                tableName: "StockCountLines",
                indexName: "UX_StockCountLines_Sheet_Owner_Item_Location_Lot"));
            migrationBuilder.Sql(DropIndexIfExists(
                tableName: "StockCountLines",
                indexName: "UX_StockCountLines_Sheet_Owner_Item_Location_NoBatch"));
            migrationBuilder.Sql(DropCheckConstraintIfExists(
                tableName: "StockReservations",
                constraintName: "CK_StockReservations_Qty_ClosedWithinReserved"));
            migrationBuilder.Sql(DropIndexIfExists(
                tableName: "StockReservations",
                indexName: "IX_StockReservations_VoucherId_VoucherDetailId_OwnerPartnerId_ItemId_LocationId_LotNumber_ExpiryDate"));
            migrationBuilder.Sql(CreateIndexIfMissing(
                tableName: "StockReservations",
                indexName: "IX_StockReservations_VoucherId_VoucherDetailId_ItemId_LocationId_LotNumber_ExpiryDate",
                columns: "[VoucherId], [VoucherDetailId], [ItemId], [LocationId], [LotNumber], [ExpiryDate]",
                unique: true,
                filter: "[Status] = 1"));
        }

        private static string ResolveSchemaDeclaration(string tableName)
            => $@"
DECLARE @schema sysname;
SELECT TOP (1) @schema = s.name
FROM sys.tables t
INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE t.name = N'{tableName}'
ORDER BY CASE WHEN s.name = SCHEMA_NAME() THEN 0 WHEN s.name = N'dbo' THEN 1 ELSE 2 END;";

        private static string ResolvePrincipalSchemaDeclaration(string tableName)
            => $@"
DECLARE @principalSchema sysname;
SELECT TOP (1) @principalSchema = s.name
FROM sys.tables t
INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE t.name = N'{tableName}'
ORDER BY CASE WHEN s.name = SCHEMA_NAME() THEN 0 WHEN s.name = N'dbo' THEN 1 ELSE 2 END;";

        private static string DropForeignKeyIfExists(string tableName, string constraintName)
            => ResolveSchemaDeclaration(tableName) + $@"
IF @schema IS NOT NULL
   AND EXISTS (
       SELECT 1
       FROM sys.foreign_keys fk
       WHERE fk.parent_object_id = OBJECT_ID(QUOTENAME(@schema) + N'.[{tableName}]')
         AND fk.name = N'{constraintName}')
BEGIN
    DECLARE @sql nvarchar(max) =
        N'ALTER TABLE ' + QUOTENAME(@schema) + N'.[{tableName}] DROP CONSTRAINT [{constraintName}]';
    EXEC sys.sp_executesql @sql;
END";

        private static string AddForeignKeyIfMissing(
            string tableName,
            string constraintName,
            string columnName,
            string principalTableName,
            string principalColumnName)
            => ResolveSchemaDeclaration(tableName) + ResolvePrincipalSchemaDeclaration(principalTableName) + $@"
IF @schema IS NOT NULL
   AND @principalSchema IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.foreign_keys fk
       WHERE fk.parent_object_id = OBJECT_ID(QUOTENAME(@schema) + N'.[{tableName}]')
         AND fk.name = N'{constraintName}')
BEGIN
    DECLARE @sql nvarchar(max) =
        N'ALTER TABLE ' + QUOTENAME(@schema) + N'.[{tableName}] WITH CHECK ADD CONSTRAINT [{constraintName}] FOREIGN KEY ([{columnName}]) REFERENCES '
        + QUOTENAME(@principalSchema) + N'.[{principalTableName}] ([{principalColumnName}]);'
        + N' ALTER TABLE ' + QUOTENAME(@schema) + N'.[{tableName}] CHECK CONSTRAINT [{constraintName}];';
    EXEC sys.sp_executesql @sql;
END";

        private static string CreateIndexIfMissing(
            string tableName,
            string indexName,
            string columns,
            bool unique = false,
            string filter = null)
        {
            var uniqueSql = unique ? "UNIQUE " : string.Empty;
            var filterSql = string.IsNullOrWhiteSpace(filter) ? string.Empty : $" WHERE {filter}";

            return ResolveSchemaDeclaration(tableName) + $@"
IF @schema IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes i
       WHERE i.object_id = OBJECT_ID(QUOTENAME(@schema) + N'.[{tableName}]')
         AND i.name = N'{indexName}')
BEGIN
    DECLARE @sql nvarchar(max) =
        N'CREATE {uniqueSql}INDEX [{indexName}] ON ' + QUOTENAME(@schema) + N'.[{tableName}] ({columns}){filterSql}';
    EXEC sys.sp_executesql @sql;
END";
        }

        private static string DropIndexIfExists(string tableName, string indexName)
            => ResolveSchemaDeclaration(tableName) + $@"
IF @schema IS NOT NULL
   AND EXISTS (
       SELECT 1
       FROM sys.indexes i
       WHERE i.object_id = OBJECT_ID(QUOTENAME(@schema) + N'.[{tableName}]')
         AND i.name = N'{indexName}')
BEGIN
    DECLARE @sql nvarchar(max) =
        N'DROP INDEX [{indexName}] ON ' + QUOTENAME(@schema) + N'.[{tableName}]';
    EXEC sys.sp_executesql @sql;
END";

        private static string AddCheckConstraintIfMissing(
            string tableName,
            string constraintName,
            string predicate)
            => ResolveSchemaDeclaration(tableName) + $@"
IF @schema IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.check_constraints cc
       WHERE cc.parent_object_id = OBJECT_ID(QUOTENAME(@schema) + N'.[{tableName}]')
         AND cc.name = N'{constraintName}')
BEGIN
    DECLARE @sql nvarchar(max) =
        N'ALTER TABLE ' + QUOTENAME(@schema) + N'.[{tableName}] WITH CHECK ADD CONSTRAINT [{constraintName}] CHECK ({predicate});'
        + N' ALTER TABLE ' + QUOTENAME(@schema) + N'.[{tableName}] CHECK CONSTRAINT [{constraintName}];';
    EXEC sys.sp_executesql @sql;
END";

        private static string DropCheckConstraintIfExists(string tableName, string constraintName)
            => ResolveSchemaDeclaration(tableName) + $@"
IF @schema IS NOT NULL
   AND EXISTS (
       SELECT 1
       FROM sys.check_constraints cc
       WHERE cc.parent_object_id = OBJECT_ID(QUOTENAME(@schema) + N'.[{tableName}]')
         AND cc.name = N'{constraintName}')
BEGIN
    DECLARE @sql nvarchar(max) =
        N'ALTER TABLE ' + QUOTENAME(@schema) + N'.[{tableName}] DROP CONSTRAINT [{constraintName}]';
    EXEC sys.sp_executesql @sql;
END";
    }
}
