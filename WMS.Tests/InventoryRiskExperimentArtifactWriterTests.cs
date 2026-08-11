using WMS.Services;
using WMS.ViewModels;

namespace WMS.Tests;

public sealed class InventoryRiskExperimentArtifactWriterTests
{
    [Fact]
    public async Task WriteAsync_ShouldBeDeterministicPseudonymizedAndCsvSafe()
    {
        var root = Path.Combine(Path.GetTempPath(), $"WMS-AI4-{Guid.NewGuid():N}");
        var firstDirectory = Path.Combine(root, "first");
        var secondDirectory = Path.Combine(root, "second");
        try
        {
            var request = CreateRequest(firstDirectory);
            var writer = new InventoryRiskExperimentArtifactWriter();

            var first = await writer.WriteAsync(request);
            var second = await writer.WriteAsync(CreateRequest(secondDirectory));

            Assert.Equal(first.ArtifactHashes, second.ArtifactHashes);
            var firstFiles = Directory.GetFiles(firstDirectory).Select(Path.GetFileName).OrderBy(name => name).ToArray();
            var secondFiles = Directory.GetFiles(secondDirectory).Select(Path.GetFileName).OrderBy(name => name).ToArray();
            Assert.Equal(firstFiles, secondFiles);
            foreach (var file in firstFiles)
            {
                var firstBytes = await File.ReadAllBytesAsync(Path.Combine(firstDirectory, file!));
                var secondBytes = await File.ReadAllBytesAsync(Path.Combine(secondDirectory, file!));
                Assert.Equal(firstBytes, secondBytes);
            }

            var bundleText = string.Join(
                "\n",
                Directory.GetFiles(firstDirectory).Select(File.ReadAllText));
            Assert.DoesNotContain("RAW-LOT-SECRET", bundleText, StringComparison.Ordinal);
            Assert.DoesNotContain("RAW-SOURCE-WATERMARK", bundleText, StringComparison.Ordinal);
            Assert.DoesNotContain("987654321", bundleText, StringComparison.Ordinal);
            Assert.Contains("'=HYPERLINK", bundleText, StringComparison.Ordinal);
            Assert.Contains("'+CMD", bundleText, StringComparison.Ordinal);
            Assert.Contains("MATERIAL_VARIANCE_LABEL_UNAVAILABLE_WITHOUT_THRESHOLD_SNAPSHOT", bundleText, StringComparison.Ordinal);
            Assert.Contains("DATASET_CONTAINS_DEMO_DATA", bundleText, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WriteAsync_ShouldGenerateRedactedScopedInvocation()
    {
        var request = CreateRequest(Path.Combine(Path.GetTempPath(), $"WMS-AI4-{Guid.NewGuid():N}"));
        try
        {
            await new InventoryRiskExperimentArtifactWriter().WriteAsync(request);
            var executionLog = await File.ReadAllTextAsync(
                Path.Combine(request.OutputDirectory, "benchmark.log"));

            Assert.Contains("--scope-from-secure-input", executionLog, StringComparison.Ordinal);
            Assert.DoesNotContain("987654321", executionLog, StringComparison.Ordinal);
            Assert.DoesNotContain("ConnectionStrings", executionLog, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Server=", executionLog, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(request.OutputDirectory))
                Directory.Delete(request.OutputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task WriteAsync_ShouldRejectNonHashSourceMetadata()
    {
        var request = CreateRequest(Path.Combine(Path.GetTempPath(), $"WMS-AI4-{Guid.NewGuid():N}"));
        request = new InventoryRiskExperimentArtifactRequest
        {
            OutputDirectory = request.OutputDirectory,
            Dataset = request.Dataset,
            Split = request.Split,
            Benchmark = request.Benchmark,
            SourceHashes = new Dictionary<string, string> { ["source.cs"] = "not-a-sha256-value" }
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => new InventoryRiskExperimentArtifactWriter().WriteAsync(request));

        Assert.Contains("SHA-256", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(request.OutputDirectory));
    }

    [Fact]
    public async Task WriteAsync_WhenCancelled_ShouldPreserveExistingArtifactDirectory()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"WMS-AI4-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDirectory);
        var markerPath = Path.Combine(outputDirectory, "previous-run.marker");
        await File.WriteAllTextAsync(markerPath, "preserve");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        try
        {
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => new InventoryRiskExperimentArtifactWriter().WriteAsync(
                    CreateRequest(outputDirectory),
                    cancelled.Token));

            Assert.True(File.Exists(markerPath));
            Assert.Equal("preserve", await File.ReadAllTextAsync(markerPath));
            Assert.Single(Directory.GetFiles(outputDirectory));
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
                Directory.Delete(outputDirectory, recursive: true);
        }
    }

    private static InventoryRiskExperimentArtifactRequest CreateRequest(string outputDirectory)
    {
        var train = CreateRow("SAMPLE-TRAIN", "ENTITY-TRAIN", new DateTime(2025, 1, 1), false);
        var validation = CreateRow("SAMPLE-VALIDATION", "ENTITY-VALIDATION", new DateTime(2025, 5, 1), true);
        var test = CreateRow("SAMPLE-TEST", "ENTITY-TEST", new DateTime(2025, 9, 1), true, isDemoData: true);
        var rows = new[] { train, validation, test };
        var query = new InventoryRiskDatasetQuery
        {
            BuildAsOf = new DateTime(2026, 1, 1),
            OutcomeHorizonDays = 30,
            Seed = 20260716,
            IncludeIsolatedTestData = true,
            IncludeDemoData = true,
            AllowedWarehouseIds = new[] { 987654321 },
            AllowedOwnerPartnerIds = new[] { 987654321 }
        };
        var dataset = new InventoryRiskDatasetBuildResult
        {
            Status = InventoryRiskExperimentStatus.BlockedData,
            Query = query,
            Rows = rows,
            Exclusions = new Dictionary<string, int> { ["DRAFT_OUTCOME"] = 2 },
            ReadinessCodes = new[] { "DATASET_INSUFFICIENT_ROWS" },
            DatasetHash = new string('D', 64),
            SourceWatermark = "RAW-SOURCE-WATERMARK",
            CandidateSnapshotCount = 5,
            CandidateOutcomeCount = 4,
            PositiveCount = 2,
            NegativeCount = 1,
            DemoRowCount = 1,
            DistinctPredictionDays = 3
        };
        var split = new InventoryRiskTemporalSplitResult
        {
            Status = InventoryRiskExperimentStatus.BlockedData,
            Configuration = new InventoryRiskTemporalSplitConfiguration
            {
                TrainEnd = new DateTime(2025, 1, 31),
                ValidationStart = new DateTime(2025, 5, 1),
                ValidationEnd = new DateTime(2025, 5, 31),
                TestStart = new DateTime(2025, 9, 1),
                TestEnd = new DateTime(2025, 9, 30),
                OutcomeHorizonDays = 30
            },
            TrainRows = new[] { train },
            ValidationRows = new[] { validation },
            TestRows = new[] { test },
            ReadinessCodes = new[] { "TEST_INSUFFICIENT_ROWS_FOR_PRECISION_AT_100" },
            EmbargoExcludedCount = 1,
            PurgedEntityOverlapCount = 2,
            SplitHash = new string('E', 64)
        };
        var benchmark = new InventoryRiskBenchmarkService().Evaluate(split.TestRows, query.Seed);
        return new InventoryRiskExperimentArtifactRequest
        {
            OutputDirectory = outputDirectory,
            Dataset = dataset,
            Split = split,
            Benchmark = benchmark,
            SourceHashes = new Dictionary<string, string>
            {
                ["Services/InventoryRiskDatasetService.cs"] = new string('A', 64),
                ["packages.lock.json"] = new string('B', 64)
            }
        };
    }

    private static InventoryRiskDatasetRow CreateRow(
        string sampleKey,
        string entityKey,
        DateTime predictionCutoff,
        bool positive,
        bool isDemoData = false)
        => new()
        {
            FeatureSnapshotId = 987654321,
            StockCountSheetId = 987654321,
            StockCountLineId = 987654321,
            WarehouseId = 987654321,
            OwnerPartnerId = 987654321,
            ItemId = 987654321,
            LocationId = 987654321,
            LotNumber = "RAW-LOT-SECRET",
            SourceWatermark = "RAW-SOURCE-WATERMARK",
            SampleKey = sampleKey,
            EntityKeyHash = entityKey,
            PredictionCutoff = predictionCutoff,
            OutcomeCountedAt = predictionCutoff.AddDays(6),
            OutcomeApprovedAt = predictionCutoff.AddDays(7),
            DatasetSchemaVersion = InventoryRiskDatasetQuery.CurrentDatasetSchemaVersion,
            FeatureSchemaVersion = "AI-FEATURE-SCHEMA-0.1",
            ModelVersion = "=HYPERLINK(\"https://invalid\")",
            FeatureHash = new string('C', 64),
            Features = new InventoryRiskFeatureVector
            {
                OnHandBaseQty = 10m,
                AvailableBaseQty = 10m,
                AbcClass = "+CMD"
            },
            RuleRiskScore = positive ? 90m : 10m,
            BaseUomCode = "CAI",
            SystemBaseQty = 10m,
            CountedBaseQty = positive ? 9m : 10m,
            VarianceBaseQty = positive ? -1m : 0m,
            AbsoluteVarianceBaseQty = positive ? 1m : 0m,
            HasQuantityVariance = positive,
            HasMaterialVariance = null,
            IsDemoData = isDemoData,
            EstimatedEffortMinutes = 5
        };
}
