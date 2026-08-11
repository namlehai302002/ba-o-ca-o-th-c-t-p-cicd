using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WMS.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerScopeToCycleCountSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_CycleCountSchedules_ProgramId_ItemId_LocationId] ON [CycleCountSchedules];");

            migrationBuilder.AddColumn<int>(
                name: "OwnerPartnerId",
                table: "CycleCountSchedules",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountSchedules_OwnerPartnerId",
                table: "CycleCountSchedules",
                column: "OwnerPartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountSchedules_ProgramId_OwnerPartnerId_ItemId_LocationId",
                table: "CycleCountSchedules",
                columns: new[] { "ProgramId", "OwnerPartnerId", "ItemId", "LocationId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CycleCountSchedules_Partners_OwnerPartnerId",
                table: "CycleCountSchedules",
                column: "OwnerPartnerId",
                principalTable: "Partners",
                principalColumn: "PartnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CycleCountSchedules_Partners_OwnerPartnerId",
                table: "CycleCountSchedules");

            migrationBuilder.DropIndex(
                name: "IX_CycleCountSchedules_OwnerPartnerId",
                table: "CycleCountSchedules");

            migrationBuilder.DropIndex(
                name: "IX_CycleCountSchedules_ProgramId_OwnerPartnerId_ItemId_LocationId",
                table: "CycleCountSchedules");

            migrationBuilder.DropColumn(
                name: "OwnerPartnerId",
                table: "CycleCountSchedules");

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountSchedules_ProgramId_ItemId_LocationId",
                table: "CycleCountSchedules",
                columns: new[] { "ProgramId", "ItemId", "LocationId" },
                unique: true);
        }
    }
}
