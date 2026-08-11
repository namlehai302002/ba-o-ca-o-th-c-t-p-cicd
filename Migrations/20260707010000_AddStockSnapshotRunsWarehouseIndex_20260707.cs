using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WMS.Data;

#nullable disable

namespace WMS.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260707010000_AddStockSnapshotRunsWarehouseIndex_20260707")]
    public partial class AddStockSnapshotRunsWarehouseIndex_20260707 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[StockSnapshotRuns]', N'U') IS NOT NULL
   AND COL_LENGTH(N'[StockSnapshotRuns]', N'WarehouseId') IS NOT NULL
   AND NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'[StockSnapshotRuns]')
          AND name = N'IX_StockSnapshotRuns_WarehouseId'
   )
BEGIN
    CREATE INDEX [IX_StockSnapshotRuns_WarehouseId]
    ON [StockSnapshotRuns] ([WarehouseId]);
END");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[StockSnapshotRuns]', N'U') IS NOT NULL
   AND EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'[StockSnapshotRuns]')
          AND name = N'IX_StockSnapshotRuns_WarehouseId'
   )
BEGIN
    DROP INDEX [IX_StockSnapshotRuns_WarehouseId] ON [StockSnapshotRuns];
END");
        }
    }
}
