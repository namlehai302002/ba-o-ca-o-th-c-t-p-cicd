using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WMS.Migrations
{
    /// <inheritdoc />
    public partial class AddCycleCountRecommendationWorkflow_AI3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CycleCountRecommendations",
                columns: table => new
                {
                    CycleCountRecommendationId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InventoryRiskPredictionId = table.Column<long>(type: "bigint", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    OwnerPartnerId = table.Column<int>(type: "int", nullable: true),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    LocationId = table.Column<int>(type: "int", nullable: false),
                    LotNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ExpiryDate = table.Column<DateTime>(type: "date", nullable: true),
                    ScopeKey = table.Column<string>(type: "nvarchar(360)", maxLength: 360, nullable: false),
                    PriorityScore = table.Column<decimal>(type: "decimal(9,4)", nullable: true),
                    SnapshotSystemQty = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    EstimatedEffortMinutes = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<byte>(type: "tinyint", nullable: false),
                    IsBlindCount = table.Column<bool>(type: "bit", nullable: false),
                    AssignedTo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    WorkPool = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    SnapshotWatermark = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    PredictionCutoff = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FreshUntil = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionReasonCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    StockCountSheetId = table.Column<long>(type: "bigint", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CycleCountRecommendations", x => x.CycleCountRecommendationId);
                    table.ForeignKey(
                        name: "FK_CycleCountRecommendations_InventoryRiskPredictions_InventoryRiskPredictionId",
                        column: x => x.InventoryRiskPredictionId,
                        principalTable: "InventoryRiskPredictions",
                        principalColumn: "InventoryRiskPredictionId");
                    table.ForeignKey(
                        name: "FK_CycleCountRecommendations_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "ItemId");
                    table.ForeignKey(
                        name: "FK_CycleCountRecommendations_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "LocationId");
                    table.ForeignKey(
                        name: "FK_CycleCountRecommendations_Partners_OwnerPartnerId",
                        column: x => x.OwnerPartnerId,
                        principalTable: "Partners",
                        principalColumn: "PartnerId");
                    table.ForeignKey(
                        name: "FK_CycleCountRecommendations_StockCountSheets_StockCountSheetId",
                        column: x => x.StockCountSheetId,
                        principalTable: "StockCountSheets",
                        principalColumn: "StockCountSheetId");
                    table.ForeignKey(
                        name: "FK_CycleCountRecommendations_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "WarehouseId");
                });

            migrationBuilder.CreateTable(
                name: "CycleCountRecommendationDecisions",
                columns: table => new
                {
                    CycleCountRecommendationDecisionId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CycleCountRecommendationId = table.Column<long>(type: "bigint", nullable: false),
                    DecisionType = table.Column<byte>(type: "tinyint", nullable: false),
                    FromState = table.Column<byte>(type: "tinyint", nullable: true),
                    ToState = table.Column<byte>(type: "tinyint", nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(360)", maxLength: 360, nullable: false),
                    ModelVersion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CycleCountRecommendationDecisions", x => x.CycleCountRecommendationDecisionId);
                    table.ForeignKey(
                        name: "FK_CycleCountRecommendationDecisions_CycleCountRecommendations_CycleCountRecommendationId",
                        column: x => x.CycleCountRecommendationId,
                        principalTable: "CycleCountRecommendations",
                        principalColumn: "CycleCountRecommendationId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountRecommendationDecisions_Recommendation_Time",
                table: "CycleCountRecommendationDecisions",
                columns: new[] { "CycleCountRecommendationId", "DecidedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountRecommendations_ItemId",
                table: "CycleCountRecommendations",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountRecommendations_LocationId",
                table: "CycleCountRecommendations",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountRecommendations_OwnerPartnerId",
                table: "CycleCountRecommendations",
                column: "OwnerPartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountRecommendations_Scope_State_Fresh",
                table: "CycleCountRecommendations",
                columns: new[] { "WarehouseId", "OwnerPartnerId", "State", "FreshUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountRecommendations_ScopeKey_State",
                table: "CycleCountRecommendations",
                columns: new[] { "ScopeKey", "State" });

            migrationBuilder.CreateIndex(
                name: "UX_CycleCountRecommendations_Prediction",
                table: "CycleCountRecommendations",
                column: "InventoryRiskPredictionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_CycleCountRecommendations_StockCountSheet",
                table: "CycleCountRecommendations",
                column: "StockCountSheetId",
                unique: true,
                filter: "[StockCountSheetId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CycleCountRecommendationDecisions");

            migrationBuilder.DropTable(
                name: "CycleCountRecommendations");
        }
    }
}
