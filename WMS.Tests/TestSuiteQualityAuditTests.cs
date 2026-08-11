using System.Text.RegularExpressions;

namespace WMS.Tests;

public sealed class TestSuiteQualityAuditTests
{
    [Fact]
    public void AutomatedTests_ShouldNotContainFocusedSkippedOrDummyPassPatterns()
    {
        var root = FindRepositoryRoot();
        var files = EnumerateTestFiles(root)
            .Where(path => !path.EndsWith(nameof(TestSuiteQualityAuditTests) + ".cs", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var forbiddenPatterns = new[]
        {
            "test." + "only",
            "describe." + "only",
            "it." + "only",
            "fit" + "(",
            "xit" + "(",
            "Assert.True(" + "true",
            "Assert.False(" + "false"
        };

        var failures = new List<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var pattern in forbiddenPatterns)
            {
                if (text.Contains(pattern, StringComparison.Ordinal))
                    failures.Add($"{Path.GetRelativePath(root, file)} contains forbidden test-quality pattern `{pattern}`.");
            }

            if (Regex.IsMatch(text, @"\[(Fact|Theory)\s*\(\s*Skip\s*=", RegexOptions.CultureInvariant))
                failures.Add($"{Path.GetRelativePath(root, file)} contains skipped xUnit test.");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void PlaywrightSkips_ShouldBeJustifiedViewportOrSideEffectGuards()
    {
        var root = FindRepositoryRoot();
        var visualFiles = Directory.EnumerateFiles(Path.Combine(root, "tests", "visual"), "*.ts", SearchOption.TopDirectoryOnly);
        var skipLines = visualFiles
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => new { File = file, Line = line.Trim(), LineNumber = index + 1 }))
            .Where(x => x.Line.Contains("test.skip(", StringComparison.Ordinal))
            .ToArray();

        Assert.True(skipLines.Length <= 28, $"Unexpected number of Playwright skips: {skipLines.Length}.");

        var failures = new List<string>();
        foreach (var skip in skipLines)
        {
            var hasReason = skip.Line.Contains(", '", StringComparison.Ordinal) || skip.Line.Contains(", \"", StringComparison.Ordinal);
            var reasonLooksIntentional = skip.Line.Contains("desktop", StringComparison.OrdinalIgnoreCase)
                || skip.Line.Contains("mobile", StringComparison.OrdinalIgnoreCase)
                || skip.Line.Contains("viewport", StringComparison.OrdinalIgnoreCase)
                || skip.Line.Contains("mutating DB", StringComparison.OrdinalIgnoreCase)
                || skip.Line.Contains("one authenticated", StringComparison.OrdinalIgnoreCase)
                || skip.Line.Contains("checked on primary", StringComparison.OrdinalIgnoreCase);

            if (!hasReason || !reasonLooksIntentional)
                failures.Add($"{Path.GetRelativePath(root, skip.File)}:{skip.LineNumber} has an unjustified Playwright skip: {skip.Line}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void CoreRiskAreas_ShouldHaveBugDrivenBackendAndBrowserCoverage()
    {
        var root = FindRepositoryRoot();
        var allTests = string.Join(Environment.NewLine, EnumerateTestFiles(root).Select(File.ReadAllText));

        var requiredEvidenceTokens = new[]
        {
            "Inbound_ShouldRejectBadQuantitiesAndScopeThenPostLotExpiryStock",
            "Outbound_ShouldReserveFefoPickPostAndBlockWhenStockChangedAfterReservation",
            "Transfer_ShouldRespectWarehouseScopeAndMoveLotExpiryToDestination",
            "ReturnFlows_ShouldCustomerReturnIncreaseAndSupplierReturnDeductStock",
            "SerialLotExpiryAndCancellation_ShouldPreserveScopeAndReleaseOpenReservations",
            "CatchWeight_ShouldRequirePositiveConsistentBaseAndActualWeight",
            "UomConversion_ShouldPreferItemSpecificAndRejectZeroReverseRate",
            "VoucherCreateSubmit_ShouldNotLeaveButtonLoadingWhenCustomValidationStopsPost",
            "VoucherCreatePost_ShouldRejectInactiveBaseOrUnmappedTransactionUom",
            "ApplyAsync_ShouldRunThreeDomainsReplaceWarehouseDataAndPreserveLoginData",
            "DirectIdReadExportAndMutation_ShouldReturnSafeEnvelopeOutsideApiScope",
            "LaborManagement_ShouldReturnExistingActivityWhenParallelCaptureCreatesSameSource",
            "voucher create validation does not leave submit button loading",
            "voucher OCR applies header and matched lines from document result",
            "voucher OCR does not overwrite manually entered header fields",
            "voucher manual row does not inherit OCR trace and survives OCR replacement",
            "voucher Excel import previews rows and blocks applying the same file twice",
            "outbound voucher exposes FEFO source lot and location selection surface",
            "voucher create can submit after validation failure and blocks double submit request",
            "voucher OCR shows friendly error and restores upload button on provider failure",
            "voucher details dock modal decodes Vietnamese transport fields",
            "quick search degrades safely when barcode lookup is unavailable",
            "offline queue exposes a safe degraded state when local storage is blocked",
            "mobile deep audit",
            "same-origin 5xx responses",
            "console errors"
        };

        foreach (var token in requiredEvidenceTokens)
            Assert.Contains(token, allTests, StringComparison.Ordinal);
    }

    [Fact]
    public void TestSuite_ShouldIncludeRealDatabaseAndServiceExecutionCoverage()
    {
        var root = FindRepositoryRoot();
        var allTests = string.Join(Environment.NewLine, EnumerateTestFiles(root).Select(File.ReadAllText));

        var requiredExecutionMarkers = new[]
        {
            "UseInMemoryDatabase",
            "UseSqlite",
            "CompleteInboundAsync",
            "ReleaseVoucherForPickingAsync",
            "PostReservedOutboundAsync",
            "ConfirmPickTaskAsync",
            "ApplyAsync(DemoDataDomain",
            "GateInAsync",
            "CalculateChargeAsync",
            "Assert.ThrowsAsync<BusinessRuleException>"
        };

        foreach (var marker in requiredExecutionMarkers)
            Assert.Contains(marker, allTests, StringComparison.Ordinal);
    }

    [Fact]
    public void MutationEndpoints_ShouldKeepGlobalCsrfAndVoucherAjaxTokenCoverage()
    {
        var root = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(root, "Program.cs"));
        var voucherCreate = File.ReadAllText(Path.Combine(root, "Views", "Vouchers", "Create.cshtml"));
        var voucherIndexController = File.ReadAllText(Path.Combine(root, "Controllers", "VouchersController.Index.cs"));
        var voucherHelperController = File.ReadAllText(Path.Combine(root, "Controllers", "VouchersController.Helpers.cs"));

        Assert.Contains("options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute())", program, StringComparison.Ordinal);
        Assert.Contains("options.HeaderName = \"RequestVerificationToken\"", program, StringComparison.Ordinal);

        Assert.Contains("id=\"voucherForm\"", voucherCreate, StringComparison.Ordinal);
        Assert.Contains("@Html.AntiForgeryToken()", voucherCreate, StringComparison.Ordinal);
        Assert.Contains("document.querySelector('#voucherForm input[name=\"__RequestVerificationToken\"]')?.value", voucherCreate, StringComparison.Ordinal);
        Assert.Contains("'RequestVerificationToken': antiForgeryToken", voucherCreate, StringComparison.Ordinal);

        Assert.Contains("[HttpPost]", voucherIndexController, StringComparison.Ordinal);
        Assert.Contains("public async Task<IActionResult> Create(VoucherCreateViewModel vm", voucherIndexController, StringComparison.Ordinal);
        Assert.Contains("public async Task<IActionResult> SuggestPutaway([FromBody] List<PutawayRequest> items)", voucherHelperController, StringComparison.Ordinal);
    }

    private static IEnumerable<string> EnumerateTestFiles(string root)
    {
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "WMS.Tests"), "*.cs", SearchOption.TopDirectoryOnly))
            yield return file;

        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "tests", "visual"), "*.ts", SearchOption.TopDirectoryOnly))
            yield return file;
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WMS.sln")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new DirectoryNotFoundException("Cannot find repository root.");
    }
}
