using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WMS.Data;

#nullable disable

namespace WMS.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260713010000_RepairCoreUniqueIndexes_Gate2")]
public sealed class RepairCoreUniqueIndexes_Gate2 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(RepairIndexSql(
            "ItemCategories", "UX_G2Repair_ItemCategories_CategoryCode", "[CategoryCode]",
            "GROUP BY [CategoryCode] HAVING COUNT(*) > 1"));
        migrationBuilder.Sql(RepairIndexSql(
            "UnitsOfMeasure", "UX_G2Repair_UnitsOfMeasure_UomCode", "[UomCode]",
            "GROUP BY [UomCode] HAVING COUNT(*) > 1"));
        migrationBuilder.Sql(RepairIndexSql(
            "Warehouses", "UX_G2Repair_Warehouses_WarehouseCode", "[WarehouseCode]",
            "GROUP BY [WarehouseCode] HAVING COUNT(*) > 1"));
        migrationBuilder.Sql(RepairIndexSql(
            "Partners", "UX_G2Repair_Partners_PartnerCode", "[PartnerCode]",
            "GROUP BY [PartnerCode] HAVING COUNT(*) > 1"));
        migrationBuilder.Sql(RepairIndexSql(
            "AppUsers", "UX_G2Repair_AppUsers_UserName", "[UserName]",
            "GROUP BY [UserName] HAVING COUNT(*) > 1"));
        migrationBuilder.Sql(RepairIndexSql(
            "UnitConversions", "UX_G2Repair_UnitConversions_Item_From_To", "[ItemId], [FromUomId], [ToUomId]",
            "WHERE [ItemId] IS NOT NULL GROUP BY [ItemId], [FromUomId], [ToUomId] HAVING COUNT(*) > 1",
            "[ItemId] IS NOT NULL"));
        migrationBuilder.Sql(RepairIndexSql(
            "UnitConversions", "UX_G2Repair_UnitConversions_Global_From_To", "[FromUomId], [ToUomId]",
            "WHERE [ItemId] IS NULL GROUP BY [FromUomId], [ToUomId] HAVING COUNT(*) > 1",
            "[ItemId] IS NULL"));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var (table, index) in RepairedIndexes)
        {
            migrationBuilder.Sql($@"
IF OBJECT_ID(N'[dbo].[{table}]', N'U') IS NOT NULL
   AND EXISTS
   (
       SELECT 1 FROM sys.indexes
       WHERE object_id = OBJECT_ID(N'[dbo].[{table}]')
         AND name = N'{index}'
   )
BEGIN
    DROP INDEX [{index}] ON [dbo].[{table}];
END");
        }
    }

    private static string RepairIndexSql(
        string table,
        string repairIndex,
        string columns,
        string duplicateQuerySuffix,
        string filter = null)
        => $@"
IF OBJECT_ID(N'[dbo].[{table}]', N'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1
       FROM sys.indexes i
       WHERE i.object_id = OBJECT_ID(N'[dbo].[{table}]')
         AND i.is_unique = 1
         AND i.is_disabled = 0
         AND
         (
             SELECT STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal)
             FROM sys.index_columns ic
             JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
             WHERE ic.object_id = i.object_id
               AND ic.index_id = i.index_id
               AND ic.key_ordinal > 0
         ) = N'{columns.Replace("[", string.Empty).Replace("]", string.Empty).Replace(" ", string.Empty)}'
   )
BEGIN
    IF EXISTS (SELECT 1 FROM [dbo].[{table}] {duplicateQuerySuffix})
        THROW 51020, 'Gate 2 unique-index repair stopped because duplicate business keys exist.', 1;

    CREATE UNIQUE INDEX [{repairIndex}]
    ON [dbo].[{table}] ({columns}){(filter == null ? string.Empty : $" WHERE {filter}")};
END";

    private static readonly (string Table, string Index)[] RepairedIndexes =
    {
        ("ItemCategories", "UX_G2Repair_ItemCategories_CategoryCode"),
        ("UnitsOfMeasure", "UX_G2Repair_UnitsOfMeasure_UomCode"),
        ("Warehouses", "UX_G2Repair_Warehouses_WarehouseCode"),
        ("Partners", "UX_G2Repair_Partners_PartnerCode"),
        ("AppUsers", "UX_G2Repair_AppUsers_UserName"),
        ("UnitConversions", "UX_G2Repair_UnitConversions_Item_From_To"),
        ("UnitConversions", "UX_G2Repair_UnitConversions_Global_From_To")
    };
}
