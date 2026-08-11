using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WMS.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryRiskShadowBaseline_AI2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InventoryRiskModelVersions",
                columns: table => new
                {
                    InventoryRiskModelVersionId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ModelKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ModelType = table.Column<byte>(type: "tinyint", nullable: false),
                    LifecycleStatus = table.Column<byte>(type: "tinyint", nullable: false),
                    FeatureSchemaVersion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    TrainingCutoff = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConfigurationJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ArtifactHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryRiskModelVersions", x => x.InventoryRiskModelVersionId);
                });

            migrationBuilder.CreateTable(
                name: "InventoryRiskFeatureSnapshots",
                columns: table => new
                {
                    InventoryRiskFeatureSnapshotId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InventoryRiskModelVersionId = table.Column<long>(type: "bigint", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PredictionCutoff = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    OwnerPartnerId = table.Column<int>(type: "int", nullable: true),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    LocationId = table.Column<int>(type: "int", nullable: false),
                    LotNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ExpiryDate = table.Column<DateTime>(type: "date", nullable: true),
                    ScopeKey = table.Column<string>(type: "nvarchar(360)", maxLength: 360, nullable: false),
                    FeatureJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FeatureHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceWatermark = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    DataQualityStatus = table.Column<byte>(type: "tinyint", nullable: false),
                    DataQualityCodes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryRiskFeatureSnapshots", x => x.InventoryRiskFeatureSnapshotId);
                    table.ForeignKey(
                        name: "FK_InventoryRiskFeatureSnapshots_InventoryRiskModelVersions_InventoryRiskModelVersionId",
                        column: x => x.InventoryRiskModelVersionId,
                        principalTable: "InventoryRiskModelVersions",
                        principalColumn: "InventoryRiskModelVersionId");
                    table.ForeignKey(
                        name: "FK_InventoryRiskFeatureSnapshots_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "ItemId");
                    table.ForeignKey(
                        name: "FK_InventoryRiskFeatureSnapshots_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "LocationId");
                    table.ForeignKey(
                        name: "FK_InventoryRiskFeatureSnapshots_Partners_OwnerPartnerId",
                        column: x => x.OwnerPartnerId,
                        principalTable: "Partners",
                        principalColumn: "PartnerId");
                    table.ForeignKey(
                        name: "FK_InventoryRiskFeatureSnapshots_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "WarehouseId");
                });

            migrationBuilder.CreateTable(
                name: "InventoryRiskPredictions",
                columns: table => new
                {
                    InventoryRiskPredictionId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InventoryRiskFeatureSnapshotId = table.Column<long>(type: "bigint", nullable: false),
                    InventoryRiskModelVersionId = table.Column<long>(type: "bigint", nullable: false),
                    RiskScore = table.Column<decimal>(type: "decimal(9,4)", nullable: true),
                    Severity = table.Column<byte>(type: "tinyint", nullable: true),
                    ReasonCodesJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FreshUntil = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsShadowMode = table.Column<bool>(type: "bit", nullable: false),
                    OutputHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryRiskPredictions", x => x.InventoryRiskPredictionId);
                    table.ForeignKey(
                        name: "FK_InventoryRiskPredictions_InventoryRiskFeatureSnapshots_InventoryRiskFeatureSnapshotId",
                        column: x => x.InventoryRiskFeatureSnapshotId,
                        principalTable: "InventoryRiskFeatureSnapshots",
                        principalColumn: "InventoryRiskFeatureSnapshotId");
                    table.ForeignKey(
                        name: "FK_InventoryRiskPredictions_InventoryRiskModelVersions_InventoryRiskModelVersionId",
                        column: x => x.InventoryRiskModelVersionId,
                        principalTable: "InventoryRiskModelVersions",
                        principalColumn: "InventoryRiskModelVersionId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryRiskFeatureSnapshots_ItemId",
                table: "InventoryRiskFeatureSnapshots",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryRiskFeatureSnapshots_LocationId",
                table: "InventoryRiskFeatureSnapshots",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryRiskFeatureSnapshots_OwnerPartnerId",
                table: "InventoryRiskFeatureSnapshots",
                column: "OwnerPartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryRiskFeatureSnapshots_Scope_Cutoff",
                table: "InventoryRiskFeatureSnapshots",
                columns: new[] { "WarehouseId", "OwnerPartnerId", "PredictionCutoff" });

            migrationBuilder.CreateIndex(
                name: "UX_InventoryRiskFeatureSnapshots_Batch_Scope",
                table: "InventoryRiskFeatureSnapshots",
                columns: new[] { "InventoryRiskModelVersionId", "BatchId", "ScopeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_InventoryRiskModelVersions_Key_Version",
                table: "InventoryRiskModelVersions",
                columns: new[] { "ModelKey", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryRiskPredictions_Model_Time_Score",
                table: "InventoryRiskPredictions",
                columns: new[] { "InventoryRiskModelVersionId", "GeneratedAt", "RiskScore" });

            migrationBuilder.CreateIndex(
                name: "UX_InventoryRiskPredictions_FeatureSnapshot",
                table: "InventoryRiskPredictions",
                column: "InventoryRiskFeatureSnapshotId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "InventoryRiskPredictions");
            migrationBuilder.DropTable(name: "InventoryRiskFeatureSnapshots");
            migrationBuilder.DropTable(name: "InventoryRiskModelVersions");
        }
    }
}
