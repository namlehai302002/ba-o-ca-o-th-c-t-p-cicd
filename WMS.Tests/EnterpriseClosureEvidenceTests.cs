using System.Text;
using System.Text.RegularExpressions;

namespace WMS.Tests;

public sealed class EnterpriseClosureEvidenceTests
{
    [Fact]
    public void ClosureDocuments_ShouldDefineStateMachineUatAndExternalEvidenceBoundaries()
    {
        var root = FindRepositoryRoot();
        var stateMachine = ReadUtf8Strict(Path.Combine(root, "docs", "VOUCHER_STATE_MACHINE.md"));
        var uat = ReadUtf8Strict(Path.Combine(root, "docs", "UAT_WMS_ROLE_CHECKLIST.md"));
        var deviceMatrix = ReadUtf8Strict(Path.Combine(root, "docs", "DEVICE_MATRIX_AND_INTEGRATION_PLAN.md"));

        foreach (var token in new[]
        {
            "Inbound State Machine",
            "Outbound State Machine",
            "Transfer State Machine",
            "Adjustment And Stock Count",
            "Forbidden Transitions",
            "Draft",
            "PendingApproval",
            "Receiving",
            "WaitingForPick",
            "Completed",
            "Cancelled"
        })
        {
            Assert.Contains(token, stateMachine, StringComparison.Ordinal);
        }

        foreach (var token in new[] { "Admin", "Manager", "Warehouse Staff", "Viewer", "Cross-role Negative Tests", "Sign-off", "Evidence" })
            Assert.Contains(token, uat, StringComparison.Ordinal);

        foreach (var token in new[]
        {
            "Barcode scanner",
            "RF handheld",
            "Label printer",
            "Scale",
            "ERP/accounting",
            "OMS/e-commerce",
            "TMS/carrier",
            "MHE/WCS",
            "Chua xac minh",
            "Minimum Pass Criteria Before Production Claim"
        })
        {
            Assert.Contains(token, deviceMatrix, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DataQualityAndLoadScripts_ShouldBeReadOnlyRedactedAndOptInForWrites()
    {
        var root = FindRepositoryRoot();
        var sql = ReadUtf8Strict(Path.Combine(root, "scripts", "WmsDataQualityAudit.sql"));
        var rbacSql = ReadUtf8Strict(Path.Combine(root, "scripts", "WmsRbacReadOnlyAudit.sql"));
        var capacitySql = ReadUtf8Strict(Path.Combine(root, "scripts", "WmsItemLocationCapacityReadOnlyAudit.sql"));
        var ps = ReadUtf8Strict(Path.Combine(root, "scripts", "Invoke-WmsDataQualityAudit.ps1"));
        var k6 = ReadUtf8Strict(Path.Combine(root, "scripts", "k6-wms-core-load.js"));

        Assert.Contains("Read-only script", sql, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"^\s*(UPDATE|DELETE|MERGE|DROP|TRUNCATE|ALTER|CREATE)\b", RegexOptions.IgnoreCase | RegexOptions.Multiline), sql);
        foreach (var issueCode in new[]
        {
            "ITEM_BASE_UOM_INVALID",
            "CURRENT_STOCK_MISMATCH",
            "SERIAL_ACTIVE_DUPLICATE",
            "POSTED_VOUCHER_WITHOUT_LEDGER",
            "OPEN_OUTBOUND_WITH_OVER_RESERVATION"
        })
        {
            Assert.Contains(issueCode, sql, StringComparison.Ordinal);
        }

        Assert.Contains("WMS_DATA_QUALITY_SQL_CONNECTION_STRING", ps, StringComparison.Ordinal);
        Assert.Contains("ApplicationIntent", ps, StringComparison.Ordinal);
        Assert.Contains("TargetFingerprint", ps, StringComparison.Ordinal);
        Assert.Contains("SummaryOnly", ps, StringComparison.Ordinal);
        Assert.Contains("Read-only guard rejected SQL token", ps, StringComparison.Ordinal);
        Assert.Contains("ValidateSqlOnly", ps, StringComparison.Ordinal);
        Assert.Contains("Get-SqlGuardText", ps, StringComparison.Ordinal);
        Assert.Contains("StringLiteral", ps, StringComparison.Ordinal);
        Assert.Contains("INTO|DBCC|BACKUP|RESTORE", ps, StringComparison.Ordinal);
        Assert.Contains("Never pass secrets in shell history", ps, StringComparison.Ordinal);
        Assert.DoesNotContain("Write-Output $resolvedConnectionString", ps, StringComparison.Ordinal);
        Assert.DoesNotContain("Write-Host $resolvedConnectionString", ps, StringComparison.Ordinal);

        Assert.DoesNotMatch(new Regex(@"^\s*(INSERT|UPDATE|DELETE|MERGE|DROP|TRUNCATE|ALTER|CREATE|EXEC|GRANT|REVOKE|DENY)\b", RegexOptions.IgnoreCase | RegexOptions.Multiline), rbacSql);
        foreach (var issueCode in new[]
        {
            "RBAC_MISSING_ROLE",
            "RBAC_ADMIN_MISSING_PERMISSION",
            "RBAC_EXPECTED_GRANT_MISSING",
            "RBAC_UNEXPECTED_NON_ADMIN_GRANT",
            "RBAC_DUPLICATE_ROLE_OR_PERMISSION",
            "RBAC_ROLE_SUMMARY"
        })
        {
            Assert.Contains(issueCode, rbacSql, StringComparison.Ordinal);
        }

        Assert.DoesNotMatch(new Regex(@"^\s*(INSERT|UPDATE|DELETE|MERGE|DROP|TRUNCATE|ALTER|CREATE|EXEC|GRANT|REVOKE|DENY)\b", RegexOptions.IgnoreCase | RegexOptions.Multiline), capacitySql);
        foreach (var issueCode in new[]
        {
            "ITEMLOCATIONS_TABLE_MISSING",
            "ITEMLOCATIONS_MAX_CAPACITY_MISSING",
            "ITEMLOCATIONS_MAX_CAPACITY_SHAPE_MISMATCH",
            "ITEMLOCATIONS_TOTAL_CAPACITY_MISSING"
        })
        {
            Assert.Contains(issueCode, capacitySql, StringComparison.Ordinal);
        }

        foreach (var token in new[] { "smoke_10", "steady_50", "stress_100", "peak_200", "WMS_K6_ENABLE_WRITES" })
            Assert.Contains(token, k6, StringComparison.Ordinal);
    }

    [Fact]
    public void HistoricalMigrations_ShouldOnlyRollbackSchemaTheyOwn()
    {
        var root = FindRepositoryRoot();
        var initial = ReadUtf8Strict(Path.Combine(root, "Migrations", "20260326164214_AddDefectQty.cs"));
        var uomGroup = ReadUtf8Strict(Path.Combine(root, "Migrations", "20260408063102_AddUomGroup.cs"));
        var lotAndRowVersion = ReadUtf8Strict(Path.Combine(root, "Migrations", "20260414070330_AddLotAndRowVersionAndBatchIndexes.cs"));
        var locationCapacity = ReadUtf8Strict(Path.Combine(root, "Migrations", "20260424150236_AddLocationMaxCapacity.cs"));
        var itemLocationCapacityRepair = ReadUtf8Strict(Path.Combine(root, "Migrations", "20260712010000_EnsureItemLocationCapacityColumns_20260712.cs"));

        Assert.Contains("LotNumber = table.Column<string>", initial, StringComparison.Ordinal);
        Assert.Contains("table.PrimaryKey(\"PK_VoucherDetails\"", initial, StringComparison.Ordinal);

        Assert.DoesNotMatch(
            new Regex(@"migrationBuilder\.AddColumn<string>\(\s*name:\s*\""LotNumber\"",\s*table:\s*\""VoucherDetails\""", RegexOptions.Singleline),
            lotAndRowVersion);
        Assert.DoesNotMatch(
            new Regex(@"migrationBuilder\.DropColumn\(\s*name:\s*\""LotNumber\"",\s*table:\s*\""VoucherDetails\""", RegexOptions.Singleline),
            lotAndRowVersion);

        foreach (var column in new[] { "LotNumber", "MaxCapacity", "TotalCapacity" })
        {
            Assert.DoesNotMatch(
                new Regex($@"migrationBuilder\.DropColumn\(\s*name:\s*\""{column}\"",\s*table:\s*\""ItemLocations\""", RegexOptions.Singleline),
                uomGroup);
        }

        var downStart = lotAndRowVersion.IndexOf("protected override void Down", StringComparison.Ordinal);
        Assert.True(downStart > 0, "The migration must expose an explicit rollback path.");
        var up = lotAndRowVersion[..downStart];
        var down = lotAndRowVersion[downStart..];

        Assert.Contains(
            "DROP INDEX [IX_ItemLocations_ItemId_LocationId_LotNumber] ON [dbo].[ItemLocations]",
            up,
            StringComparison.Ordinal);
        Assert.Matches(
            new Regex(@"CreateIndex\(\s*name:\s*\""IX_ItemLocations_ItemId_LocationId_LotNumber\""[^;]+filter:\s*\""\[LotNumber\] IS NOT NULL\""", RegexOptions.Singleline),
            down);
        Assert.DoesNotMatch(
            new Regex(@"CreateIndex\(\s*name:\s*\""IX_ItemLocations_ItemId_LocationId\""[^;]+columns:\s*new\[\]\s*\{\s*\""ItemId\"",\s*\""LocationId\""\s*\}", RegexOptions.Singleline),
            down);

        Assert.DoesNotMatch(
            new Regex(@"migrationBuilder\.AddColumn<decimal>\(\s*name:\s*\""MaxCapacity\"",\s*table:\s*\""Locations\""", RegexOptions.Singleline),
            locationCapacity);
        Assert.DoesNotMatch(
            new Regex(@"migrationBuilder\.DropColumn\(\s*name:\s*\""MaxCapacity\"",\s*table:\s*\""Locations\""", RegexOptions.Singleline),
            locationCapacity);
        Assert.Contains("UPDATE [Locations] SET [MaxCapacity] = 0 WHERE [MaxCapacity] IS NULL", locationCapacity, StringComparison.Ordinal);
        Assert.Matches(
            new Regex(@"AlterColumn<decimal>\(\s*name:\s*\""MaxCapacity\"",\s*table:\s*\""Locations\""[^;]+nullable:\s*false[^;]+oldNullable:\s*true", RegexOptions.Singleline),
            locationCapacity);

        Assert.Contains("COL_LENGTH", itemLocationCapacityRepair, StringComparison.Ordinal);
        Assert.Contains("[MaxCapacity] decimal(18,4) NULL", itemLocationCapacityRepair, StringComparison.Ordinal);
        Assert.Contains("[TotalCapacity] decimal(18,4) NULL", itemLocationCapacityRepair, StringComparison.Ordinal);
        Assert.Contains("intentionally non-destructive", itemLocationCapacityRepair, StringComparison.Ordinal);
        Assert.DoesNotContain("DropColumn", itemLocationCapacityRepair, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP COLUMN", itemLocationCapacityRepair, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RealE2ePlaywrightGate_ShouldBeOptInAndProtectedFromAccidentalDbWrites()
    {
        var root = FindRepositoryRoot();
        var packageJson = ReadUtf8Strict(Path.Combine(root, "package.json"));
        var authSetup = ReadUtf8Strict(Path.Combine(root, "tests", "visual", "auth.setup.ts"));
        var config = ReadUtf8Strict(Path.Combine(root, "tests", "visual", "playwright.real-e2e.config.ts"));
        var spec = ReadUtf8Strict(Path.Combine(root, "tests", "visual", "wms-core-real-e2e.spec.ts"));

        Assert.Contains("\"e2e:real\"", packageJson, StringComparison.Ordinal);
        Assert.Contains("wms-core-real-e2e.spec.ts", config, StringComparison.Ordinal);
        Assert.Contains("WMS_REAL_E2E", spec, StringComparison.Ordinal);
        Assert.Contains("WMS_REAL_E2E_WRITE", spec, StringComparison.Ordinal);
        Assert.Contains("test.describe.skip", spec, StringComparison.Ordinal);
        Assert.Contains("writeChecksEnabled", spec, StringComparison.Ordinal);
        Assert.Contains("same-origin HTTP 5xx", spec, StringComparison.Ordinal);
        Assert.Contains("submit button should recover after validation failure", spec, StringComparison.Ordinal);
        Assert.Contains("cancel must not submit demo data form", spec, StringComparison.Ordinal);
        Assert.Contains("WMS_REAL_E2E_CREATOR_STATE", spec, StringComparison.Ordinal);
        Assert.Contains("WMS_REAL_E2E_APPROVER_STATE", spec, StringComparison.Ordinal);
        Assert.Contains("four-eyes separation of duties", spec, StringComparison.Ordinal);
        Assert.Contains("E2E-${Date.now()}", spec, StringComparison.Ordinal);
        Assert.Contains("ApproveInbound", spec, StringComparison.Ordinal);
        Assert.Contains("ConfirmReceiving", spec, StringComparison.Ordinal);
        Assert.Contains("ConfirmActualReceivingQty", spec, StringComparison.Ordinal);
        Assert.Contains("PostReservedOutbound", spec, StringComparison.Ordinal);
        Assert.DoesNotContain("\u00C3", spec, StringComparison.Ordinal);
        Assert.DoesNotContain("\u00E1\u00BA", spec, StringComparison.Ordinal);

        Assert.Contains("page.url().includes('/Account/SetupAdmin')", authSetup, StringComparison.Ordinal);
        Assert.Contains("isLoopbackBaseUrl(baseUrl)", authSetup, StringComparison.Ordinal);
        Assert.Contains("input[name=\"userName\"]", authSetup, StringComparison.Ordinal);
        Assert.Contains("input[name=\"fullName\"]", authSetup, StringComparison.Ordinal);
        Assert.Contains("input[name=\"password\"]", authSetup, StringComparison.Ordinal);
        Assert.Contains("WMS_TEST_RESET_TOKEN", authSetup, StringComparison.Ordinal);
        Assert.Contains("input[name=\"newPassword\"]", authSetup, StringComparison.Ordinal);
        Assert.Contains(".AspNetCore.Cookies", authSetup, StringComparison.Ordinal);
        Assert.Contains("authenticated WMS route", authSetup, StringComparison.Ordinal);
    }

    [Fact]
    public void GoalClosureReport_ShouldRecordEvidenceAndUnverifiedProductionBoundaries()
    {
        var root = FindRepositoryRoot();
        var reportPath = Path.Combine(root, "FINAL_WMS_ENTERPRISE_QA_REPORT.md");
        Assert.True(File.Exists(reportPath), "FINAL_WMS_ENTERPRISE_QA_REPORT.md must exist at repository root.");

        var report = ReadUtf8Strict(reportPath);
        foreach (var token in new[]
        {
            "Goal Closure Boundary",
            "repo/local build",
            "Bằng Chứng Kiểm Thử Gần Nhất",
            "Runtime artifact cleanup",
            "Chưa thể xác minh local",
            "appsettings.json",
            "7A3E4A74C0D7D7CBA0AF5EB91A65B06764CCDF38B79798D9C0063188A3C4A1EC",
            "RF scanner thật",
            "máy in tem thật",
            "không tuyên bố production hoàn hảo"
        })
        {
            Assert.Contains(token, report, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("Password=", report, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WMS.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Cannot find repository root.");
    }

    private static string ReadUtf8Strict(string path)
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        return encoding.GetString(File.ReadAllBytes(path));
    }
}
