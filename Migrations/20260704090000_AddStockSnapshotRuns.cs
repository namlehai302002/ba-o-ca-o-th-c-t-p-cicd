using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WMS.Data;

#nullable disable

namespace WMS.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260704090000_AddStockSnapshotRuns")]
    public partial class AddStockSnapshotRuns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_StockSnapshots_SnapshotDate_ItemId_WarehouseId_OwnerPartnerId] ON [StockSnapshots];");

            migrationBuilder.CreateTable(
                name: "StockSnapshotRuns",
                columns: table => new
                {
                    StockSnapshotRunId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    SnapshotDate = table.Column<DateTime>(type: "date", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TotalItems = table.Column<int>(type: "int", nullable: false),
                    TotalValue = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockSnapshotRuns", x => x.StockSnapshotRunId);
                    table.ForeignKey(
                        name: "FK_StockSnapshotRuns_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "WarehouseId");
                });

            migrationBuilder.AddColumn<long>(
                name: "StockSnapshotRunId",
                table: "StockSnapshots",
                type: "bigint",
                nullable: true);

            migrationBuilder.Sql(@"
INSERT INTO [StockSnapshotRuns] ([WarehouseId], [SnapshotDate], [CreatedBy], [CreatedAt], [TotalItems], [TotalValue], [Status])
SELECT
    [WarehouseId],
    [SnapshotDate],
    N'system-migration',
    MAX([CreatedAt]),
    COUNT(1),
    SUM([TotalValue]),
    N'Completed'
FROM [StockSnapshots]
GROUP BY [WarehouseId], [SnapshotDate];

UPDATE s
SET [StockSnapshotRunId] = r.[StockSnapshotRunId]
FROM [StockSnapshots] s
INNER JOIN [StockSnapshotRuns] r
    ON r.[WarehouseId] = s.[WarehouseId]
    AND r.[SnapshotDate] = s.[SnapshotDate];");

            migrationBuilder.CreateIndex(
                name: "IX_StockSnapshotRuns_Warehouse_Date_CreatedAt",
                table: "StockSnapshotRuns",
                columns: new[] { "WarehouseId", "SnapshotDate", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StockSnapshots_Date_Item_Warehouse_Owner",
                table: "StockSnapshots",
                columns: new[] { "SnapshotDate", "ItemId", "WarehouseId", "OwnerPartnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockSnapshots_StockSnapshotRunId",
                table: "StockSnapshots",
                column: "StockSnapshotRunId");

            migrationBuilder.CreateIndex(
                name: "IX_StockSnapshots_StockSnapshotRunId_ItemId_OwnerPartnerId",
                table: "StockSnapshots",
                columns: new[] { "StockSnapshotRunId", "ItemId", "OwnerPartnerId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_StockSnapshots_StockSnapshotRuns_StockSnapshotRunId",
                table: "StockSnapshots",
                column: "StockSnapshotRunId",
                principalTable: "StockSnapshotRuns",
                principalColumn: "StockSnapshotRunId",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "Cannot safely downgrade AddStockSnapshotRuns because multiple snapshot runs per warehouse/date may exist. " +
                "Restore from backup or write a deliberate data-retention downgrade script for the target environment.");
        }
    }
}
