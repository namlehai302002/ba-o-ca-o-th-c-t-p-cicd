using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WMS.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerScopeToSlottingSimulationAndMovementTaskGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_SlottingSimulationLines_Scenario_Item_Move] ON [SlottingSimulationLines];");
            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_MovementTasks_Open_Item_DuplicateGuard] ON [MovementTasks];");

            migrationBuilder.AddColumn<int>(
                name: "OwnerPartnerId",
                table: "SlottingSimulationLines",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SlottingSimulationLines_OwnerPartnerId",
                table: "SlottingSimulationLines",
                column: "OwnerPartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_SlottingSimulationLines_Scenario_Item_Move",
                table: "SlottingSimulationLines",
                columns: new[] { "ScenarioId", "OwnerPartnerId", "ItemId", "SourceLocationId", "SuggestedLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_MovementTasks_Open_Item_DuplicateGuard",
                table: "MovementTasks",
                columns: new[] { "MovementMode", "OwnerPartnerId", "ItemId", "SourceLocationId", "DestinationLocationId", "TaskType" },
                unique: true,
                filter: "[Status] IN (1, 2, 3) AND [MovementMode] = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_SlottingSimulationLines_Partners_OwnerPartnerId",
                table: "SlottingSimulationLines",
                column: "OwnerPartnerId",
                principalTable: "Partners",
                principalColumn: "PartnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SlottingSimulationLines_Partners_OwnerPartnerId",
                table: "SlottingSimulationLines");

            migrationBuilder.DropIndex(
                name: "IX_SlottingSimulationLines_OwnerPartnerId",
                table: "SlottingSimulationLines");

            migrationBuilder.DropIndex(
                name: "IX_SlottingSimulationLines_Scenario_Item_Move",
                table: "SlottingSimulationLines");

            migrationBuilder.DropIndex(
                name: "IX_MovementTasks_Open_Item_DuplicateGuard",
                table: "MovementTasks");

            migrationBuilder.DropColumn(
                name: "OwnerPartnerId",
                table: "SlottingSimulationLines");

            migrationBuilder.CreateIndex(
                name: "IX_SlottingSimulationLines_Scenario_Item_Move",
                table: "SlottingSimulationLines",
                columns: new[] { "ScenarioId", "ItemId", "SourceLocationId", "SuggestedLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_MovementTasks_Open_Item_DuplicateGuard",
                table: "MovementTasks",
                columns: new[] { "MovementMode", "ItemId", "SourceLocationId", "DestinationLocationId", "TaskType" },
                unique: true,
                filter: "[Status] IN (1, 2, 3) AND [MovementMode] = 1");
        }
    }
}
