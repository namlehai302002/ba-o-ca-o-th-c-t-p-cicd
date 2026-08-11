using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WMS.Data;

#nullable disable

namespace WMS.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260712010000_EnsureItemLocationCapacityColumns_20260712")]
    public partial class EnsureItemLocationCapacityColumns_20260712 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DECLARE @schema sysname;
SELECT TOP (1) @schema = s.name
FROM sys.tables t
INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE t.name = N'ItemLocations'
ORDER BY CASE WHEN s.name = SCHEMA_NAME() THEN 0 WHEN s.name = N'dbo' THEN 1 ELSE 2 END;

IF @schema IS NULL
    THROW 51000, 'ItemLocations table was not found; capacity compatibility repair cannot continue.', 1;

DECLARE @qualifiedTable nvarchar(517) = QUOTENAME(@schema) + N'.[ItemLocations]';

IF COL_LENGTH(@qualifiedTable, N'MaxCapacity') IS NULL
BEGIN
    DECLARE @addMaxCapacity nvarchar(max) =
        N'ALTER TABLE ' + @qualifiedTable + N' ADD [MaxCapacity] decimal(18,4) NULL;';
    EXEC sys.sp_executesql @addMaxCapacity;
END;

IF COL_LENGTH(@qualifiedTable, N'TotalCapacity') IS NULL
BEGIN
    DECLARE @addTotalCapacity nvarchar(max) =
        N'ALTER TABLE ' + @qualifiedTable + N' ADD [TotalCapacity] decimal(18,4) NULL;';
    EXEC sys.sp_executesql @addTotalCapacity;
END;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Compatibility repair is intentionally non-destructive because either column may
            // have existed before this migration on an independently provisioned database.
        }
    }
}
