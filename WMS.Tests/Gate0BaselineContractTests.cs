using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WMS.Tests;

public sealed class Gate0BaselineContractTests
{
    [Fact]
    public void InventorySourceOfTruthContract_ShouldMatchCurrentRuntime()
    {
        var root = FindRepositoryRoot();
        var contract = ReadUtf8(Path.Combine(root, "docs", "domain", "inventory-source-of-truth.md"));
        var itemLocation = ReadUtf8(Path.Combine(root, "Models", "ItemLocation.cs"));
        var item = ReadUtf8(Path.Combine(root, "Models", "Item.cs"));
        var voucherDetail = ReadUtf8(Path.Combine(root, "Models", "VoucherDetail.cs"));
        var conversion = ReadUtf8(Path.Combine(root, "Models", "UnitConversion.cs"));
        var balance = ReadUtf8(Path.Combine(root, "Services", "InventoryBalanceService.cs"));
        var reservation = ReadUtf8(Path.Combine(root, "Services", "InventoryReservationService.cs"));
        var dbContext = ReadUtf8(Path.Combine(root, "Data", "AppDbContext.cs"));
        var vietnamTime = ReadUtf8(Path.Combine(root, "Common", "VietnamTime.cs"));

        foreach (var token in new[]
        {
            "`ItemLocation` là nguồn sự thật",
            "`InventoryTransaction` là ledger bất biến",
            "`Item.CurrentStock` và `Item.TotalStockValue` là cache tổng hợp",
            "AvailableQty = Quantity - ReservedQty",
            "WarehouseId` (suy ra từ `Location -> Zone`)",
            "BaseQty = TransactionQty x ConversionRate",
            "decimal(18,4)",
            "decimal(18,6)",
            "MidpointRounding.AwayFromZero",
            "Asia/Ho_Chi_Minh",
            "`<= LockDate`"
        })
        {
            Assert.Contains(token, contract, StringComparison.Ordinal);
        }

        Assert.Contains("public decimal AvailableQty => Quantity - ReservedQty;", itemLocation, StringComparison.Ordinal);
        Assert.Contains("[Column(TypeName = \"decimal(18,4)\")]", itemLocation, StringComparison.Ordinal);
        Assert.Contains("public decimal CurrentStock", item, StringComparison.Ordinal);
        Assert.Contains("public decimal BaseQty", voucherDetail, StringComparison.Ordinal);
        Assert.Contains("[Column(TypeName = \"decimal(18,6)\")]", conversion, StringComparison.Ordinal);
        Assert.Contains("GroupBy(il => il.ItemId)", balance, StringComparison.Ordinal);
        Assert.Contains("item.CurrentStock = computedStock", balance, StringComparison.Ordinal);
        Assert.Contains("r.ReservedQty - r.ConsumedQty - r.ReleasedQty", reservation, StringComparison.Ordinal);
        Assert.Contains("Database.BeginTransactionAsync", dbContext, StringComparison.Ordinal);
        Assert.Contains("BuildInventoryTransactionsAsync", dbContext, StringComparison.Ordinal);
        Assert.Contains("CK_ItemLocations_Qty_NonNegative", dbContext, StringComparison.Ordinal);
        Assert.Contains("Asia/Ho_Chi_Minh", vietnamTime, StringComparison.Ordinal);
    }

    [Fact]
    public void StagingConfiguration_ShouldDocumentPublishBoundaryWithoutProtectedValues()
    {
        var root = FindRepositoryRoot();
        var staging = ReadUtf8(Path.Combine(root, "artifacts", "baseline", "staging-configuration.md"));
        var packageScript = ReadUtf8(Path.Combine(root, "scripts", "Build-ProductionPackage.ps1"));
        var program = ReadUtf8(Path.Combine(root, "Program.cs"));

        foreach (var token in new[]
        {
            "ASPNETCORE_ENVIRONMENT",
            "ASPNETCORE_URLS",
            "ConnectionStrings:DefaultConnection",
            "StartupInitialization:RbacSeedEnabled",
            "BackgroundWorkers:Enabled",
            "ProductionSre:TelemetryPersistenceEnabled",
            "launchSettings.json",
            "không được dùng bởi ứng dụng đã publish",
            "không tự chạy EF migration"
        })
        {
            Assert.Contains(token, staging, StringComparison.Ordinal);
        }

        Assert.Contains("dotnet", packageScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("publish", packageScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GetConnectionString(\"DefaultConnection\")", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Database.Migrate", program, StringComparison.Ordinal);
        Assert.DoesNotContain("MigrateAsync", program, StringComparison.Ordinal);

        var protectedValues = ReadProtectedConfigurationValues(Path.Combine(root, "appsettings.json"));
        var containsProtectedValue = protectedValues.Any(value => staging.Contains(value, StringComparison.Ordinal));
        Assert.False(containsProtectedValue, "Staging artifact contains a protected appsettings value.");
    }

    [Fact]
    public void DataProfile_ShouldDefineEveryRoadmapLoadVariable()
    {
        var root = FindRepositoryRoot();
        var profile = ReadUtf8(Path.Combine(root, "artifacts", "baseline", "data-profile.md"));
        var importRuntime = ReadUtf8(Path.Combine(root, "Controllers", "VouchersController.Import.cs"));

        foreach (var variable in new[]
        {
            "U_EXPECTED",
            "U_TEST",
            "ITEM_COUNT",
            "LOCATION_COUNT",
            "LOT_SERIAL_COUNT",
            "TX_PER_DAY",
            "TX_HISTORY",
            "IMPORT_MAX_ROWS",
            "EXPORT_MAX_ROWS",
            "RPO",
            "RTO"
        })
        {
            Assert.Matches(new Regex($@"\| `{variable}` \| [^|]+ \|", RegexOptions.CultureInvariant), profile);
        }

        Assert.Contains("`U_TEST` | 20", profile, StringComparison.Ordinal);
        Assert.Contains("`IMPORT_MAX_ROWS` | 1.000", profile, StringComparison.Ordinal);
        Assert.Contains("private const int MaxVoucherImportRows = 1000;", importRuntime, StringComparison.Ordinal);
        Assert.Contains("lastUsedRow - 1 > MaxVoucherImportRows", importRuntime, StringComparison.Ordinal);
        Assert.Contains("không phải bằng chứng hosting production đã chịu tải", profile, StringComparison.Ordinal);
        Assert.Contains("RPO/RTO chỉ là target", profile, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizedClone_ShouldRemainBlockedUntilExternalPreconditionsExist()
    {
        var root = FindRepositoryRoot();
        var roadmap = ReadUtf8(Path.Combine(root, "ROADMAP_WMS_ENTERPRISE_100_PERCENT_FULL.md"));
        var runbook = ReadUtf8(Path.Combine(root, "docs", "runbooks", "SANITIZED_DATABASE_CLONE_RUNBOOK.md"));

        Assert.Contains("- [ ] Tạo thêm database từ bản sao dữ liệu hiện có đã ẩn dữ liệu nhạy cảm.", roadmap, StringComparison.Ordinal);
        Assert.Contains("`BLOCKED`", runbook, StringComparison.Ordinal);
        Assert.Contains("destination database cô lập", runbook, StringComparison.Ordinal);
        Assert.Contains("Không tạo bản sao từ database hosting", runbook, StringComparison.Ordinal);
        Assert.Contains("Chỉ sau khi đủ toàn bộ điều kiện", runbook, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> ReadProtectedConfigurationValues(string path)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        var values = new List<string>();
        Visit(json.RootElement, null, values);
        return values.Distinct(StringComparer.Ordinal).ToList();

        static void Visit(JsonElement element, string? propertyName, List<string> values)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                        Visit(property.Value, property.Name, values);
                    break;
                case JsonValueKind.Array:
                    foreach (var child in element.EnumerateArray())
                        Visit(child, propertyName, values);
                    break;
                case JsonValueKind.String when IsProtectedKey(propertyName):
                    var value = element.GetString();
                    if (!string.IsNullOrWhiteSpace(value) && value.Length >= 8)
                        values.Add(value);
                    break;
            }
        }

        static bool IsProtectedKey(string? key)
            => key != null && Regex.IsMatch(
                key,
                "(secret|password|apikey|api_key|token|credential|connectionstring)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "WMS.sln")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("WMS repository root was not found.");
    }

    private static string ReadUtf8(string path)
        => File.ReadAllText(path, new UTF8Encoding(false, true));
}
