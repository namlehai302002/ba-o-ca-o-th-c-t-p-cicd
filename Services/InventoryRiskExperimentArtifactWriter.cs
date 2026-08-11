using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WMS.Services;

public sealed class InventoryRiskExperimentArtifactRequest
{
    public string OutputDirectory { get; init; } = "";
    public InventoryRiskDatasetBuildResult Dataset { get; init; } = new();
    public InventoryRiskTemporalSplitResult Split { get; init; } = new();
    public InventoryRiskBenchmarkResult Benchmark { get; init; } = new();
    public IReadOnlyDictionary<string, string> SourceHashes { get; init; } = new Dictionary<string, string>();
}

public sealed class InventoryRiskExperimentArtifactResult
{
    public string OutputDirectory { get; init; } = "";
    public IReadOnlyDictionary<string, string> ArtifactHashes { get; init; } = new Dictionary<string, string>();
}

public interface IInventoryRiskExperimentArtifactWriter
{
    Task<InventoryRiskExperimentArtifactResult> WriteAsync(
        InventoryRiskExperimentArtifactRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Writes a deterministic, pseudonymized experiment bundle. Operational identifiers,
/// connection details and mutable source watermarks are intentionally excluded.
/// </summary>
public sealed class InventoryRiskExperimentArtifactWriter : IInventoryRiskExperimentArtifactWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public async Task<InventoryRiskExperimentArtifactResult> WriteAsync(
        InventoryRiskExperimentArtifactRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputDirectory);
        ValidateSourceHashes(request.SourceHashes);
        ValidateRequestConsistency(request);

        var outputDirectory = Path.GetFullPath(request.OutputDirectory);
        var parentDirectory = Directory.GetParent(outputDirectory)?.FullName
            ?? throw new ArgumentException("Output directory must have a parent directory.", nameof(request));
        Directory.CreateDirectory(parentDirectory);
        var stagingDirectory = Path.Combine(
            parentDirectory,
            $".{Path.GetFileName(outputDirectory)}.staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);

        try
        {
            var partitionBySampleKey = BuildPartitionMap(request.Split);
            var files = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["benchmark-results.csv"] = BuildBenchmarkCsv(request.Benchmark),
                ["benchmark.log"] = BuildExecutionSummary(request),
                ["dataset-summary.csv"] = BuildDatasetSummaryCsv(request.Dataset),
                ["experiment-manifest.json"] = BuildManifest(request),
                ["predictions-sanitized.csv"] = BuildSanitizedDatasetCsv(request.Dataset.Rows, partitionBySampleKey),
                ["split-summary.csv"] = BuildSplitSummaryCsv(request.Split)
            };

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await File.WriteAllTextAsync(
                    Path.Combine(stagingDirectory, file.Key),
                    NormalizeNewLines(file.Value),
                    Utf8NoBom,
                    cancellationToken);
            }

            var hashes = files.Keys.ToDictionary(
                name => name,
                name => HashFile(Path.Combine(stagingDirectory, name)),
                StringComparer.Ordinal);
            var hashCsv = new StringBuilder().Append(CsvRow("artifact", "sha256"));
            foreach (var hash in hashes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                hashCsv.Append(CsvRow(hash.Key, hash.Value));
            await File.WriteAllTextAsync(
                Path.Combine(stagingDirectory, "artifact-hashes.csv"),
                NormalizeNewLines(hashCsv.ToString()),
                Utf8NoBom,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            PromoteStagingDirectory(stagingDirectory, outputDirectory);
            return new InventoryRiskExperimentArtifactResult
            {
                OutputDirectory = outputDirectory,
                ArtifactHashes = new SortedDictionary<string, string>(hashes, StringComparer.Ordinal)
            };
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
                Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    private static string BuildExecutionSummary(InventoryRiskExperimentArtifactRequest request)
    {
        var datasetCodes = string.Join(',', request.Dataset.ReadinessCodes.OrderBy(code => code, StringComparer.Ordinal));
        var splitCodes = string.Join(',', request.Split.ReadinessCodes.OrderBy(code => code, StringComparer.Ordinal));
        var benchmarkCodes = string.Join(',', request.Benchmark.ReadinessCodes.OrderBy(code => code, StringComparer.Ordinal));
        return new StringBuilder()
            .Append("AI4_SAFE_INVOCATION=").AppendLine(BuildSafeInvocation(request.Dataset.Query))
            .Append("AI4_DATASET_STATUS=").AppendLine(request.Dataset.Status.ToString())
            .Append("AI4_DATASET_ROWS=").AppendLine(request.Dataset.Rows.Count.ToString(CultureInfo.InvariantCulture))
            .Append("AI4_DATASET_HASH=").AppendLine(request.Dataset.DatasetHash)
            .Append("AI4_DATASET_READINESS=").AppendLine(datasetCodes)
            .Append("AI4_SPLIT_STATUS=").AppendLine(request.Split.Status.ToString())
            .Append("AI4_SPLIT_HASH=").AppendLine(request.Split.SplitHash)
            .Append("AI4_SPLIT_READINESS=").AppendLine(splitCodes)
            .Append("AI4_BENCHMARK_STATUS=").AppendLine(request.Benchmark.Status.ToString())
            .Append("AI4_BENCHMARK_HASH=").AppendLine(request.Benchmark.BenchmarkHash)
            .Append("AI4_BENCHMARK_READINESS=").AppendLine(benchmarkCodes)
            .ToString();
    }

    private static string BuildManifest(InventoryRiskExperimentArtifactRequest request)
    {
        var dataset = request.Dataset;
        var split = request.Split;
        var benchmark = request.Benchmark;
        var manifest = new ExperimentManifest
        {
            ArtifactSchemaVersion = "AI4-EXPERIMENT-ARTIFACT-0.1",
            SafeInvocation = BuildSafeInvocation(dataset.Query),
            BuildAsOf = dataset.Query.BuildAsOf,
            Seed = dataset.Query.Seed,
            DatasetSchemaVersion = dataset.Query.DatasetSchemaVersion,
            FeatureSchemaVersion = dataset.Query.FeatureSchemaVersion,
            OutcomeHorizonDays = dataset.Query.OutcomeHorizonDays,
            IncludeIsolatedTestData = dataset.Query.IncludeIsolatedTestData,
            IncludeDemoData = dataset.Query.IncludeDemoData,
            WarehouseScopeCount = dataset.Query.AllowedWarehouseIds.Distinct().Count(),
            OwnerScopeCount = dataset.Query.AllowedOwnerPartnerIds.Distinct().Count(),
            DatasetStatus = dataset.Status.ToString(),
            DatasetHash = dataset.DatasetHash,
            DatasetRowCount = dataset.Rows.Count,
            PositiveCount = dataset.PositiveCount,
            NegativeCount = dataset.NegativeCount,
            DemoRowCount = dataset.DemoRowCount,
            DistinctPredictionDays = dataset.DistinctPredictionDays,
            DatasetReadinessCodes = dataset.ReadinessCodes.OrderBy(code => code, StringComparer.Ordinal).ToArray(),
            DatasetExclusions = new SortedDictionary<string, int>(
                dataset.Exclusions.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                StringComparer.Ordinal),
            SplitStatus = split.Status.ToString(),
            SplitHash = split.SplitHash,
            SplitReadinessCodes = split.ReadinessCodes.OrderBy(code => code, StringComparer.Ordinal).ToArray(),
            SplitConfiguration = split.Configuration == null
                ? null
                : new SplitConfigurationManifest
                {
                    TrainEnd = split.Configuration.TrainEnd,
                    ValidationStart = split.Configuration.ValidationStart,
                    ValidationEnd = split.Configuration.ValidationEnd,
                    TestStart = split.Configuration.TestStart,
                    TestEnd = split.Configuration.TestEnd,
                    OutcomeHorizonDays = split.Configuration.OutcomeHorizonDays
                },
            BenchmarkStatus = benchmark.Status.ToString(),
            BenchmarkHash = benchmark.BenchmarkHash,
            BenchmarkReadinessCodes = benchmark.ReadinessCodes.OrderBy(code => code, StringComparer.Ordinal).ToArray(),
            SourceHashes = new SortedDictionary<string, string>(
                request.SourceHashes.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                StringComparer.Ordinal),
            Limitations = BuildLimitations(dataset, split, benchmark)
        };
        return JsonSerializer.Serialize(manifest, ManifestJsonOptions) + "\n";
    }

    private static string BuildDatasetSummaryCsv(InventoryRiskDatasetBuildResult dataset)
    {
        var value = new StringBuilder().Append(CsvRow("metric", "value"));
        AddMetric(value, "status", dataset.Status.ToString());
        AddMetric(value, "dataset_hash", dataset.DatasetHash);
        AddMetric(value, "candidate_snapshot_count", dataset.CandidateSnapshotCount);
        AddMetric(value, "candidate_outcome_count", dataset.CandidateOutcomeCount);
        AddMetric(value, "row_count", dataset.Rows.Count);
        AddMetric(value, "positive_count", dataset.PositiveCount);
        AddMetric(value, "negative_count", dataset.NegativeCount);
        AddMetric(value, "demo_row_count", dataset.DemoRowCount);
        AddMetric(value, "distinct_prediction_days", dataset.DistinctPredictionDays);
        AddMetric(value, "readiness_codes", string.Join(';', dataset.ReadinessCodes.OrderBy(code => code, StringComparer.Ordinal)));
        foreach (var exclusion in dataset.Exclusions.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            AddMetric(value, $"excluded_{exclusion.Key.ToLowerInvariant()}", exclusion.Value);
        return value.ToString();
    }

    private static string BuildSplitSummaryCsv(InventoryRiskTemporalSplitResult split)
    {
        var value = new StringBuilder().Append(CsvRow(
            "partition", "status", "row_count", "positive_count", "negative_count",
            "first_cutoff", "last_cutoff", "readiness_codes"));
        AppendPartition(value, "train", split.Status, split.TrainRows, split.ReadinessCodes.Where(code => code.StartsWith("TRAIN_", StringComparison.Ordinal)));
        AppendPartition(value, "validation", split.Status, split.ValidationRows, split.ReadinessCodes.Where(code => code.StartsWith("VALIDATION_", StringComparison.Ordinal)));
        AppendPartition(value, "test", split.Status, split.TestRows, split.ReadinessCodes.Where(code => code.StartsWith("TEST_", StringComparison.Ordinal)));
        value.Append(CsvRow(
            "excluded",
            split.Status.ToString(),
            split.EmbargoExcludedCount + split.OutsideWindowCount + split.PurgedEntityOverlapCount,
            null,
            null,
            null,
            null,
            string.Join(';', split.ReadinessCodes.OrderBy(code => code, StringComparer.Ordinal))));
        return value.ToString();
    }

    private static string BuildBenchmarkCsv(InventoryRiskBenchmarkResult benchmark)
    {
        var value = new StringBuilder().Append(CsvRow(
            "candidate", "version", "is_model", "benchmark_status", "eligible_rows", "scored_rows",
            "coverage", "pr_auc_average_precision", "k", "metric_status", "positive_detected",
            "precision", "recall", "lift", "detected_abs_variance_base_qty",
            "detected_quantity_uom", "quantity_metric_status",
            "inspection_per_detected_variance", "estimated_effort_minutes"));
        foreach (var candidate in benchmark.Candidates
                     .OrderBy(candidate => candidate.CandidateName, StringComparer.Ordinal)
                     .ThenBy(candidate => candidate.CandidateVersion, StringComparer.Ordinal))
        {
            if (candidate.TopK.Count == 0)
            {
                value.Append(CsvRow(
                    candidate.CandidateName, candidate.CandidateVersion, candidate.IsModelCandidate,
                    benchmark.Status.ToString(), candidate.EligibleRowCount, candidate.ScoredRowCount,
                    candidate.Coverage, candidate.PrAucAveragePrecision, null, "NO_TOP_K_METRIC",
                    null, null, null, null, null, null, null, null, null));
                continue;
            }

            foreach (var metric in candidate.TopK.OrderBy(metric => metric.RequestedK))
            {
                value.Append(CsvRow(
                    candidate.CandidateName, candidate.CandidateVersion, candidate.IsModelCandidate,
                    benchmark.Status.ToString(), candidate.EligibleRowCount, candidate.ScoredRowCount,
                    candidate.Coverage, candidate.PrAucAveragePrecision, metric.RequestedK, metric.StatusCode,
                    metric.IsAvailable ? metric.PositiveDetected : null, metric.Precision, metric.Recall, metric.Lift,
                    metric.DetectedAbsoluteVarianceBaseQty, metric.DetectedQuantityUomCode,
                    metric.QuantityMetricStatusCode, metric.InspectionPerDetectedVariance,
                    metric.EstimatedEffortMinutes));
            }
        }
        if (benchmark.Candidates.Count == 0)
        {
            value.Append(CsvRow(
                "NO_CANDIDATE", "", false, benchmark.Status.ToString(), benchmark.EligibleRowCount,
                0, 0m, null, null, string.Join(';', benchmark.ReadinessCodes),
                null, null, null, null, null, null, null, null, null));
        }
        return value.ToString();
    }

    private static string BuildSanitizedDatasetCsv(
        IEnumerable<InventoryRiskDatasetRow> rows,
        IReadOnlyDictionary<string, string> partitionBySampleKey)
    {
        var value = new StringBuilder().Append(CsvRow(
            "record_id", "partition", "prediction_cutoff", "outcome_counted_at", "outcome_approved_at",
            "dataset_schema_version", "feature_schema_version", "model_version", "feature_hash",
            "on_hand_base_qty", "reserved_base_qty", "available_base_qty", "movement_count_30d",
            "movement_count_90d", "adjustment_abs_qty_90d", "transaction_actor_count_30d",
            "days_since_last_approved_count", "prior_count_count_180d", "prior_variance_rate_180d",
            "prior_abs_variance_qty_180d", "abc_class", "days_since_last_receipt",
            "days_since_last_outbound", "location_movement_count_30d", "location_distinct_sku_count",
            "lot_count_at_location", "days_to_expiry", "lot_tracking_flag", "expiry_tracking_flag",
            "serial_tracking_flag", "hold_qty_ratio", "rule_risk_score", "base_uom_code", "system_base_qty",
            "counted_base_qty", "variance_base_qty", "absolute_variance_base_qty", "has_quantity_variance",
            "has_material_variance", "material_variance_status", "estimated_effort_minutes",
            "is_direct_recommendation_outcome", "is_demo_data"));
        var orderedRows = rows.OrderBy(row => row.PredictionCutoff).ThenBy(row => row.SampleKey, StringComparer.Ordinal).ToArray();
        for (var index = 0; index < orderedRows.Length; index++)
        {
            var row = orderedRows[index];
            var feature = row.Features;
            value.Append(CsvRow(
                $"record-{index + 1:D6}", partitionBySampleKey.GetValueOrDefault(row.SampleKey, "excluded"),
                row.PredictionCutoff, row.OutcomeCountedAt, row.OutcomeApprovedAt,
                row.DatasetSchemaVersion, row.FeatureSchemaVersion,
                row.ModelVersion, row.FeatureHash, feature.OnHandBaseQty, feature.ReservedBaseQty,
                feature.AvailableBaseQty, feature.MovementCount30D, feature.MovementCount90D,
                feature.AdjustmentAbsQty90D, feature.TransactionActorCount30D,
                feature.DaysSinceLastApprovedCount, feature.PriorCountCount180D,
                feature.PriorVarianceRate180D, feature.PriorAbsVarianceQty180D, feature.AbcClass,
                feature.DaysSinceLastReceipt, feature.DaysSinceLastOutbound,
                feature.LocationMovementCount30D, feature.LocationDistinctSkuCount,
                feature.LotCountAtLocation, feature.DaysToExpiry, feature.LotTrackingFlag,
                feature.ExpiryTrackingFlag, feature.SerialTrackingFlag, feature.HoldQtyRatio,
                row.RuleRiskScore, row.BaseUomCode, row.SystemBaseQty, row.CountedBaseQty, row.VarianceBaseQty,
                row.AbsoluteVarianceBaseQty, row.HasQuantityVariance, row.HasMaterialVariance,
                row.MaterialVarianceStatus, row.EstimatedEffortMinutes,
                row.IsDirectRecommendationOutcome, row.IsDemoData));
        }
        return value.ToString();
    }

    private static IReadOnlyDictionary<string, string> BuildPartitionMap(InventoryRiskTemporalSplitResult split)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        AddPartition(result, split.TrainRows, "train");
        AddPartition(result, split.ValidationRows, "validation");
        AddPartition(result, split.TestRows, "test");
        return result;
    }

    private static void AddPartition(IDictionary<string, string> target, IEnumerable<InventoryRiskDatasetRow> rows, string partition)
    {
        foreach (var row in rows)
        {
            if (!target.TryAdd(row.SampleKey, partition))
                throw new InvalidOperationException($"Sample key belongs to more than one partition: {row.SampleKey}");
        }
    }

    private static void AppendPartition(
        StringBuilder target,
        string name,
        InventoryRiskExperimentStatus status,
        IReadOnlyCollection<InventoryRiskDatasetRow> rows,
        IEnumerable<string> readinessCodes)
    {
        var ordered = rows.OrderBy(row => row.PredictionCutoff).ToArray();
        target.Append(CsvRow(
            name,
            status.ToString(),
            ordered.Length,
            ordered.Count(row => row.HasQuantityVariance),
            ordered.Count(row => !row.HasQuantityVariance),
            ordered.Length == 0 ? null : ordered[0].PredictionCutoff,
            ordered.Length == 0 ? null : ordered[^1].PredictionCutoff,
            string.Join(';', readinessCodes.OrderBy(code => code, StringComparer.Ordinal))));
    }

    private static IReadOnlyList<string> BuildLimitations(
        InventoryRiskDatasetBuildResult dataset,
        InventoryRiskTemporalSplitResult split,
        InventoryRiskBenchmarkResult benchmark)
    {
        var limitations = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var code in dataset.ReadinessCodes)
            limitations.Add(code);
        foreach (var code in split.ReadinessCodes)
            limitations.Add(code);
        foreach (var code in benchmark.ReadinessCodes)
            limitations.Add(code);
        if (dataset.DemoRowCount > 0)
            limitations.Add("DATASET_CONTAINS_DEMO_DATA");
        if (dataset.Rows.Any(row => !row.HasMaterialVariance.HasValue))
            limitations.Add("MATERIAL_VARIANCE_LABEL_UNAVAILABLE_WITHOUT_THRESHOLD_SNAPSHOT");
        if (dataset.Query.AllowedWarehouseIds.Count > 0 || dataset.Query.AllowedOwnerPartnerIds.Count > 0)
            limitations.Add("SCOPE_IDENTIFIERS_REDACTED_FROM_ARTIFACT");
        limitations.Add("HISTORICAL_OUTCOME_IMMUTABILITY_REQUIRES_APPEND_ONLY_PRODUCTION_EVIDENCE");
        return limitations.ToArray();
    }

    private static void AddMetric(StringBuilder target, string metric, object? value)
        => target.Append(CsvRow(metric, value));

    private static string CsvRow(params object?[] values)
        => string.Join(',', values.Select(CsvCell)) + "\n";

    private static string CsvCell(object? value)
    {
        if (value == null)
            return "";
        var text = value switch
        {
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            decimal number => number.ToString("0.############################", CultureInfo.InvariantCulture),
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            float number => number.ToString("R", CultureInfo.InvariantCulture),
            IFormattable formattable when value is not string => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? ""
        };
        if (value is string && text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            text = "'" + text;
        return text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
            ? $"\"{text.Replace("\"", "\"\"")}\""
            : text;
    }

    private static string BuildSafeInvocation(InventoryRiskDatasetQuery query)
    {
        var value = new StringBuilder("dotnet run --project tools/WMS.Ai4.Dataset --configuration Release --no-restore --")
            .Append(" --as-of ").Append(query.BuildAsOf.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture))
            .Append(" --outcome-horizon-days ").Append(query.OutcomeHorizonDays)
            .Append(" --seed ").Append(query.Seed);
        if (query.IncludeIsolatedTestData)
            value.Append(" --include-isolated-test-data");
        if (query.IncludeDemoData)
            value.Append(" --include-demo-data");
        value.Append(query.AllowedWarehouseIds.Count == 0 && query.AllowedOwnerPartnerIds.Count == 0
            ? " --all-scopes"
            : " --scope-from-secure-input");
        return value.ToString();
    }

    private static void ValidateRequestConsistency(InventoryRiskExperimentArtifactRequest request)
    {
        var datasetRows = request.Dataset.Rows;
        if (request.Dataset.PositiveCount != datasetRows.Count(row => row.HasQuantityVariance)
            || request.Dataset.NegativeCount != datasetRows.Count(row => !row.HasQuantityVariance)
            || request.Dataset.DemoRowCount != datasetRows.Count(row => row.IsDemoData))
        {
            throw new ArgumentException("Dataset summary counts do not match dataset rows.", nameof(request));
        }

        var datasetKeys = datasetRows.Select(row => row.SampleKey).ToArray();
        if (datasetKeys.Any(string.IsNullOrWhiteSpace)
            || datasetKeys.Distinct(StringComparer.Ordinal).Count() != datasetKeys.Length)
        {
            throw new ArgumentException("Dataset sample keys must be non-empty and unique.", nameof(request));
        }

        var splitRows = request.Split.TrainRows
            .Concat(request.Split.ValidationRows)
            .Concat(request.Split.TestRows)
            .ToArray();
        var splitKeys = splitRows.Select(row => row.SampleKey).ToArray();
        if (splitKeys.Distinct(StringComparer.Ordinal).Count() != splitKeys.Length
            || splitKeys.Any(key => !datasetKeys.Contains(key, StringComparer.Ordinal)))
        {
            throw new ArgumentException("Temporal split contains duplicate or unknown dataset samples.", nameof(request));
        }

        if (request.Benchmark.EligibleRowCount != request.Split.TestRows.Count
            || request.Benchmark.PositiveCount != request.Split.TestRows.Count(row => row.HasQuantityVariance)
            || request.Benchmark.NegativeCount != request.Split.TestRows.Count(row => !row.HasQuantityVariance))
        {
            throw new ArgumentException("Benchmark summary does not match the temporal test partition.", nameof(request));
        }

        foreach (var hash in new[]
                 {
                     request.Dataset.DatasetHash,
                     request.Split.SplitHash,
                     request.Benchmark.BenchmarkHash
                 })
        {
            if (!IsSha256(hash))
                throw new ArgumentException("Dataset, split and benchmark hashes must be SHA-256 values.", nameof(request));
        }
    }

    private static void PromoteStagingDirectory(string stagingDirectory, string outputDirectory)
    {
        string? backupDirectory = null;
        if (Directory.Exists(outputDirectory))
        {
            backupDirectory = $"{outputDirectory}.backup-{Guid.NewGuid():N}";
            Directory.Move(outputDirectory, backupDirectory);
        }

        try
        {
            Directory.Move(stagingDirectory, outputDirectory);
        }
        catch
        {
            if (backupDirectory != null && Directory.Exists(backupDirectory) && !Directory.Exists(outputDirectory))
                Directory.Move(backupDirectory, outputDirectory);
            throw;
        }

        if (backupDirectory != null && Directory.Exists(backupDirectory))
            Directory.Delete(backupDirectory, recursive: true);
    }

    private static void ValidateSourceHashes(IReadOnlyDictionary<string, string> sourceHashes)
    {
        ArgumentNullException.ThrowIfNull(sourceHashes);
        foreach (var sourceHash in sourceHashes)
        {
            if (string.IsNullOrWhiteSpace(sourceHash.Key)
                || sourceHash.Key.Contains('\r')
                || sourceHash.Key.Contains('\n'))
            {
                throw new ArgumentException("Source hash paths must be non-empty single-line values.", nameof(sourceHashes));
            }
            if (string.Equals(sourceHash.Value, "MISSING", StringComparison.Ordinal))
                continue;
            if (!IsSha256(sourceHash.Value))
                throw new ArgumentException("Source hash values must be SHA-256 hex values or MISSING.", nameof(sourceHashes));
        }
    }

    private static bool IsSha256(string value)
        => value.Length == 64 && value.All(Uri.IsHexDigit);

    private static string HashFile(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static string NormalizeNewLines(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private sealed class ExperimentManifest
    {
        public string ArtifactSchemaVersion { get; init; } = "";
        public string SafeInvocation { get; init; } = "";
        public DateTime BuildAsOf { get; init; }
        public int Seed { get; init; }
        public string DatasetSchemaVersion { get; init; } = "";
        public string FeatureSchemaVersion { get; init; } = "";
        public int OutcomeHorizonDays { get; init; }
        public bool IncludeIsolatedTestData { get; init; }
        public bool IncludeDemoData { get; init; }
        public int WarehouseScopeCount { get; init; }
        public int OwnerScopeCount { get; init; }
        public string DatasetStatus { get; init; } = "";
        public string DatasetHash { get; init; } = "";
        public int DatasetRowCount { get; init; }
        public int PositiveCount { get; init; }
        public int NegativeCount { get; init; }
        public int DemoRowCount { get; init; }
        public int DistinctPredictionDays { get; init; }
        public IReadOnlyList<string> DatasetReadinessCodes { get; init; } = Array.Empty<string>();
        public IReadOnlyDictionary<string, int> DatasetExclusions { get; init; } = new Dictionary<string, int>();
        public string SplitStatus { get; init; } = "";
        public string SplitHash { get; init; } = "";
        public IReadOnlyList<string> SplitReadinessCodes { get; init; } = Array.Empty<string>();
        public SplitConfigurationManifest? SplitConfiguration { get; init; }
        public string BenchmarkStatus { get; init; } = "";
        public string BenchmarkHash { get; init; } = "";
        public IReadOnlyList<string> BenchmarkReadinessCodes { get; init; } = Array.Empty<string>();
        public IReadOnlyDictionary<string, string> SourceHashes { get; init; } = new Dictionary<string, string>();
        public IReadOnlyList<string> Limitations { get; init; } = Array.Empty<string>();
    }

    private sealed class SplitConfigurationManifest
    {
        public DateTime TrainEnd { get; init; }
        public DateTime ValidationStart { get; init; }
        public DateTime ValidationEnd { get; init; }
        public DateTime TestStart { get; init; }
        public DateTime TestEnd { get; init; }
        public int OutcomeHorizonDays { get; init; }
    }
}
