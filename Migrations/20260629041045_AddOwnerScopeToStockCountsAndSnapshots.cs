using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WMS.Data;

#nullable disable

namespace WMS.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260629041045_AddOwnerScopeToStockCountsAndSnapshots")]
    public partial class AddOwnerScopeToStockCountsAndSnapshots : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_StockSnapshots_SnapshotDate_ItemId_WarehouseId] ON [StockSnapshots];");
            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_StockCountLines_StockCountSheetId_ItemId_LocationId_LotNumber_ExpiryDate] ON [StockCountLines];");

            migrationBuilder.AddColumn<int>(
                name: "OwnerPartnerId",
                table: "StockSnapshots",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OwnerPartnerId",
                table: "StockCountLines",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockSnapshots_OwnerPartnerId",
                table: "StockSnapshots",
                column: "OwnerPartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_StockSnapshots_SnapshotDate_ItemId_WarehouseId_OwnerPartnerId",
                table: "StockSnapshots",
                columns: new[] { "SnapshotDate", "ItemId", "WarehouseId", "OwnerPartnerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockCountLines_OwnerPartnerId",
                table: "StockCountLines",
                column: "OwnerPartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_StockCountLines_StockCountSheetId_OwnerPartnerId_ItemId_LocationId_LotNumber_ExpiryDate",
                table: "StockCountLines",
                columns: new[] { "StockCountSheetId", "OwnerPartnerId", "ItemId", "LocationId", "LotNumber", "ExpiryDate" },
                unique: true,
                filter: "[LotNumber] IS NOT NULL AND [ExpiryDate] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_StockCountLines_Partners_OwnerPartnerId",
                table: "StockCountLines",
                column: "OwnerPartnerId",
                principalTable: "Partners",
                principalColumn: "PartnerId");

            migrationBuilder.AddForeignKey(
                name: "FK_StockSnapshots_Partners_OwnerPartnerId",
                table: "StockSnapshots",
                column: "OwnerPartnerId",
                principalTable: "Partners",
                principalColumn: "PartnerId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockCountLines_Partners_OwnerPartnerId",
                table: "StockCountLines");

            migrationBuilder.DropForeignKey(
                name: "FK_StockSnapshots_Partners_OwnerPartnerId",
                table: "StockSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_StockSnapshots_OwnerPartnerId",
                table: "StockSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_StockSnapshots_SnapshotDate_ItemId_WarehouseId_OwnerPartnerId",
                table: "StockSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_StockCountLines_OwnerPartnerId",
                table: "StockCountLines");

            migrationBuilder.DropIndex(
                name: "IX_StockCountLines_StockCountSheetId_OwnerPartnerId_ItemId_LocationId_LotNumber_ExpiryDate",
                table: "StockCountLines");

            migrationBuilder.DropColumn(
                name: "OwnerPartnerId",
                table: "StockSnapshots");

            migrationBuilder.DropColumn(
                name: "OwnerPartnerId",
                table: "StockCountLines");

            migrationBuilder.CreateIndex(
                name: "IX_StockSnapshots_SnapshotDate_ItemId_WarehouseId",
                table: "StockSnapshots",
                columns: new[] { "SnapshotDate", "ItemId", "WarehouseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockCountLines_StockCountSheetId_ItemId_LocationId_LotNumber_ExpiryDate",
                table: "StockCountLines",
                columns: new[] { "StockCountSheetId", "ItemId", "LocationId", "LotNumber", "ExpiryDate" },
                unique: true,
                filter: "[LotNumber] IS NOT NULL AND [ExpiryDate] IS NOT NULL");
        }
    }
}
