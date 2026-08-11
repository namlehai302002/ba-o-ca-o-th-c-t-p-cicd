using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace WMS.Services;

public sealed class InventoryRiskBenchmarkCandidate
{
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public bool IsModelCandidate { get; init; }
    public IReadOnlyDictionary<string, decimal?> ScoresBySampleKey { get; init; } = new Dictionary<string, decimal?>();
}

public sealed class InventoryRiskTopKMetric
{
    public int RequestedK { get; init; }
    public bool IsAvailable { get; init; }
    public string StatusCode { get; init; } = "";
    public int PositiveDetected { get; init; }
    public decimal? Precision { get; init; }
    public decimal? Recall { get; init; }
    public decimal? Lift { get; init; }
    public decimal? DetectedAbsoluteVarianceBaseQty { get; init; }
    public string? DetectedQuantityUomCode { get; init; }
    public string QuantityMetricStatusCode { get; init; } = "";
    public decimal? InspectionPerDetectedVariance { get; init; }
    public int? EstimatedEffortMinutes { get; init; }
}

public sealed class InventoryRiskCandidateBenchmarkResult
{
    public string CandidateName { get; init; } = "";
    public string CandidateVersion { get; init; } = "";
    public bool IsModelCandidate { get; init; }
    public int EligibleRowCount { get; init; }
    public int ScoredRowCount { get; init; }
    public decimal Coverage { get; init; }
    public decimal? PrAucAveragePrecision { get; init; }
    public IReadOnlyList<InventoryRiskTopKMetric> TopK { get; init; } = Array.Empty<InventoryRiskTopKMetric>();
}

public sealed class InventoryRiskBenchmarkResult
{
    public InventoryRiskExperimentStatus Status { get; init; }
    public int Seed { get; init; }
    public int EligibleRowCount { get; init; }
    public int PositiveCount { get; init; }
    public int NegativeCount { get; init; }
    public decimal? Prevalence { get; init; }
    public IReadOnlyList<string> ReadinessCodes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<InventoryRiskCandidateBenchmarkResult> Candidates { get; init; } = Array.Empty<InventoryRiskCandidateBenchmarkResult>();
    public string BenchmarkHash { get; init; } = "";
}

public interface IInventoryRiskBenchmarkService
{
    InventoryRiskBenchmarkResult Evaluate(
        IReadOnlyList<InventoryRiskDatasetRow> testRows,
        int seed,
        IReadOnlyList<InventoryRiskBenchmarkCandidate>? modelCandidates = null,
        IReadOnlyList<int>? topKValues = null);
}

public sealed class InventoryRiskBenchmarkService : IInventoryRiskBenchmarkService
{
    private static readonly int[] DefaultTopK = { 10, 50, 100 };

    public InventoryRiskBenchmarkResult Evaluate(
        IReadOnlyList<InventoryRiskDatasetRow> testRows,
        int seed,
        IReadOnlyList<InventoryRiskBenchmarkCandidate>? modelCandidates = null,
        IReadOnlyList<int>? topKValues = null)
    {
        ArgumentNullException.ThrowIfNull(testRows);
        modelCandidates ??= Array.Empty<InventoryRiskBenchmarkCandidate>();
        var requestedK = (topKValues ?? DefaultTopK)
            .Where(value => value > 0)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
        if (requestedK.Length == 0)
            throw new ArgumentException("At least one positive K value is required.", nameof(topKValues));

        var orderedRows = testRows
            .OrderBy(row => row.PredictionCutoff)
            .ThenBy(row => row.SampleKey, StringComparer.Ordinal)
            .ToArray();
        var positiveCount = orderedRows.Count(row => row.HasQuantityVariance);
        var negativeCount = orderedRows.Length - positiveCount;
        var readinessCodes = new List<string>();
        if (orderedRows.Length == 0)
            readinessCodes.Add("TEST_SET_EMPTY");
        if (positiveCount == 0)
            readinessCodes.Add("TEST_NO_POSITIVE_CLASS");
        if (negativeCount == 0)
            readinessCodes.Add("TEST_NO_NEGATIVE_CLASS");
        foreach (var k in requestedK.Where(k => orderedRows.Length < k))
            readinessCodes.Add($"TEST_ROWS_BELOW_K_{k}");

        var baselineCandidates = new List<InventoryRiskBenchmarkCandidate>
        {
            Candidate("RANDOM", $"SHA256-SEED-{seed}", orderedRows, row => DeterministicRandomScore(row.SampleKey, seed)),
            Candidate("ABC_DUE", "ABC-DUE-1.0", orderedRows, row => AbcDueScore(row)),
            Candidate("RULE_BASELINE", ResolveRuleVersion(orderedRows), orderedRows, row => row.RuleRiskScore)
        };
        baselineCandidates.AddRange(modelCandidates);

        var candidateResults = baselineCandidates
            .Select(candidate => EvaluateCandidate(candidate, orderedRows, positiveCount, requestedK))
            .ToArray();
        var suppliedModelCandidates = modelCandidates.Where(candidate => candidate.IsModelCandidate).ToArray();
        var hasModelCandidate = suppliedModelCandidates.Length > 0;
        var promotableModelCandidate = candidateResults.Any(result =>
            result.IsModelCandidate
            && !string.IsNullOrWhiteSpace(result.CandidateName)
            && !string.IsNullOrWhiteSpace(result.CandidateVersion)
            && result.ScoredRowCount == orderedRows.Length
            && result.Coverage == 1m
            && result.PrAucAveragePrecision.HasValue
            && result.TopK.Count == requestedK.Length
            && result.TopK.All(metric => metric.IsAvailable));
        if (!hasModelCandidate)
            readinessCodes.Add("MODEL_CANDIDATE_NOT_PROVIDED");
        else if (!promotableModelCandidate)
            readinessCodes.Add("MODEL_CANDIDATE_NOT_EVALUATION_READY");

        var status = readinessCodes.Any(code => code.StartsWith("TEST_", StringComparison.Ordinal))
            ? InventoryRiskExperimentStatus.BlockedData
            : promotableModelCandidate
                ? InventoryRiskExperimentStatus.Ready
                : InventoryRiskExperimentStatus.BaselineOnly;

        var benchmarkHash = HashBenchmark(seed, requestedK, orderedRows, baselineCandidates, candidateResults);
        return new InventoryRiskBenchmarkResult
        {
            Status = status,
            Seed = seed,
            EligibleRowCount = orderedRows.Length,
            PositiveCount = positiveCount,
            NegativeCount = negativeCount,
            Prevalence = orderedRows.Length == 0 ? null : positiveCount / (decimal)orderedRows.Length,
            ReadinessCodes = readinessCodes.Distinct(StringComparer.Ordinal).OrderBy(code => code, StringComparer.Ordinal).ToArray(),
            Candidates = candidateResults,
            BenchmarkHash = benchmarkHash
        };
    }

    private static InventoryRiskCandidateBenchmarkResult EvaluateCandidate(
        InventoryRiskBenchmarkCandidate candidate,
        IReadOnlyList<InventoryRiskDatasetRow> rows,
        int totalPositiveCount,
        IReadOnlyList<int> topKValues)
    {
        var ranked = rows
            .Select(row => new RankedRow(row, candidate.ScoresBySampleKey.GetValueOrDefault(row.SampleKey)))
            .Where(row => row.Score.HasValue)
            .OrderByDescending(row => row.Score!.Value)
            .ThenBy(row => row.Row.SampleKey, StringComparer.Ordinal)
            .ToArray();
        var prevalence = rows.Count == 0 ? (decimal?)null : totalPositiveCount / (decimal)rows.Count;
        var metrics = topKValues.Select(k => EvaluateTopK(ranked, k, totalPositiveCount, prevalence)).ToArray();
        decimal? prAuc = totalPositiveCount > 0 && rows.Count > totalPositiveCount && ranked.Length == rows.Count
            ? AveragePrecision(ranked, totalPositiveCount)
            : null;

        return new InventoryRiskCandidateBenchmarkResult
        {
            CandidateName = candidate.Name,
            CandidateVersion = candidate.Version,
            IsModelCandidate = candidate.IsModelCandidate,
            EligibleRowCount = rows.Count,
            ScoredRowCount = ranked.Length,
            Coverage = rows.Count == 0 ? 0m : ranked.Length / (decimal)rows.Count,
            PrAucAveragePrecision = prAuc,
            TopK = metrics
        };
    }

    private static InventoryRiskTopKMetric EvaluateTopK(
        IReadOnlyList<RankedRow> ranked,
        int k,
        int totalPositiveCount,
        decimal? prevalence)
    {
        if (ranked.Count < k)
        {
            return new InventoryRiskTopKMetric
            {
                RequestedK = k,
                IsAvailable = false,
                StatusCode = "INSUFFICIENT_SCORED_ROWS"
            };
        }
        if (totalPositiveCount == 0 || !prevalence.HasValue || prevalence.Value <= 0m)
        {
            return new InventoryRiskTopKMetric
            {
                RequestedK = k,
                IsAvailable = false,
                StatusCode = "POSITIVE_CLASS_UNAVAILABLE"
            };
        }

        var top = ranked.Take(k).Select(row => row.Row).ToArray();
        var detected = top.Count(row => row.HasQuantityVariance);
        var precision = detected / (decimal)k;
        var knownEffort = top.Where(row => row.EstimatedEffortMinutes.HasValue).ToArray();
        var detectedRows = top.Where(row => row.HasQuantityVariance).ToArray();
        var detectedUoms = detectedRows
            .Select(row => row.BaseUomCode?.Trim().ToUpperInvariant() ?? "")
            .ToArray();
        var distinctKnownUoms = detectedUoms
            .Where(code => code.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var canAggregateQuantity = detectedRows.Length > 0
            && detectedUoms.All(code => code.Length > 0)
            && distinctKnownUoms.Length == 1;
        var quantityMetricStatus = detectedRows.Length == 0
            ? "NO_DETECTED_VARIANCE"
            : canAggregateQuantity
                ? "AVAILABLE"
                : "MIXED_OR_UNKNOWN_UOM_SUPPRESSED";
        return new InventoryRiskTopKMetric
        {
            RequestedK = k,
            IsAvailable = true,
            StatusCode = "AVAILABLE",
            PositiveDetected = detected,
            Precision = precision,
            Recall = detected / (decimal)totalPositiveCount,
            Lift = precision / prevalence.Value,
            DetectedAbsoluteVarianceBaseQty = canAggregateQuantity
                ? detectedRows.Sum(row => row.AbsoluteVarianceBaseQty)
                : detectedRows.Length == 0 ? 0m : null,
            DetectedQuantityUomCode = canAggregateQuantity ? distinctKnownUoms[0] : null,
            QuantityMetricStatusCode = quantityMetricStatus,
            InspectionPerDetectedVariance = detected == 0 ? null : k / (decimal)detected,
            EstimatedEffortMinutes = knownEffort.Length == top.Length
                ? knownEffort.Sum(row => row.EstimatedEffortMinutes!.Value)
                : null
        };
    }

    // Step-wise area under the precision-recall curve (average precision).
    private static decimal AveragePrecision(IReadOnlyList<RankedRow> ranked, int totalPositiveCount)
    {
        decimal precisionSum = 0m;
        var positivesSeen = 0;
        for (var index = 0; index < ranked.Count; index++)
        {
            if (!ranked[index].Row.HasQuantityVariance)
                continue;
            positivesSeen++;
            precisionSum += positivesSeen / (decimal)(index + 1);
        }
        return precisionSum / totalPositiveCount;
    }

    private static InventoryRiskBenchmarkCandidate Candidate(
        string name,
        string version,
        IEnumerable<InventoryRiskDatasetRow> rows,
        Func<InventoryRiskDatasetRow, decimal?> score)
        => new()
        {
            Name = name,
            Version = version,
            ScoresBySampleKey = rows.ToDictionary(row => row.SampleKey, score, StringComparer.Ordinal)
        };

    private static decimal DeterministicRandomScore(string sampleKey, int seed)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}|{sampleKey}"));
        var raw = BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(0, sizeof(ulong)));
        return raw / (decimal)ulong.MaxValue;
    }

    private static decimal AbcDueScore(InventoryRiskDatasetRow row)
    {
        var abc = row.Features.AbcClass?.Trim().ToUpperInvariant() switch
        {
            "A" => 3m,
            "B" => 2m,
            "C" => 1m,
            _ => 0m
        };
        var days = Math.Clamp(row.Features.DaysSinceLastApprovedCount ?? 3650, 0, 3650);
        return abc * 10_000m + days;
    }

    private static string ResolveRuleVersion(IReadOnlyList<InventoryRiskDatasetRow> rows)
    {
        var versions = rows.Select(row => row.ModelVersion).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).ToArray();
        return versions.Length switch
        {
            0 => "UNAVAILABLE",
            1 => versions[0],
            _ => "MIXED_VERSION_BLOCKED"
        };
    }

    private static string HashBenchmark(
        int seed,
        IReadOnlyList<int> requestedK,
        IReadOnlyList<InventoryRiskDatasetRow> rows,
        IReadOnlyList<InventoryRiskBenchmarkCandidate> candidates,
        IReadOnlyList<InventoryRiskCandidateBenchmarkResult> results)
    {
        var value = new StringBuilder().Append(seed).Append('|')
            .Append(string.Join(',', requestedK)).Append('\n');
        foreach (var row in rows)
        {
            value.Append(row.SampleKey).Append('|')
                .Append(row.PredictionCutoff.ToString("O", CultureInfo.InvariantCulture)).Append('|')
                .Append(row.OutcomeApprovedAt.ToString("O", CultureInfo.InvariantCulture)).Append('|')
                .Append(row.HasQuantityVariance ? '1' : '0').Append('|')
                .Append(row.AbsoluteVarianceBaseQty.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(row.BaseUomCode).Append('|')
                .Append(row.EstimatedEffortMinutes?.ToString(CultureInfo.InvariantCulture) ?? "NA")
                .Append('\n');
        }
        foreach (var candidate in candidates
                     .OrderBy(candidate => candidate.Name, StringComparer.Ordinal)
                     .ThenBy(candidate => candidate.Version, StringComparer.Ordinal))
        {
            value.Append("SCORES|").Append(candidate.Name).Append('|')
                .Append(candidate.Version).Append('|')
                .Append(candidate.IsModelCandidate ? '1' : '0').Append('\n');
            foreach (var score in candidate.ScoresBySampleKey.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                value.Append(score.Key).Append('|')
                    .Append(score.Value?.ToString(CultureInfo.InvariantCulture) ?? "NA")
                    .Append('\n');
            }
        }
        foreach (var result in results
                     .OrderBy(result => result.CandidateName, StringComparer.Ordinal)
                     .ThenBy(result => result.CandidateVersion, StringComparer.Ordinal))
        {
            value.Append(result.CandidateName).Append('|')
                .Append(result.CandidateVersion).Append('|')
                .Append(result.IsModelCandidate ? '1' : '0').Append('|')
                .Append(result.EligibleRowCount).Append('|')
                .Append(result.ScoredRowCount).Append('|')
                .Append(result.Coverage.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(result.PrAucAveragePrecision?.ToString(CultureInfo.InvariantCulture) ?? "NA")
                .Append('\n');
            foreach (var metric in result.TopK.OrderBy(metric => metric.RequestedK))
            {
                value.Append(metric.RequestedK).Append('|')
                    .Append(metric.IsAvailable ? '1' : '0').Append('|')
                    .Append(metric.StatusCode).Append('|')
                    .Append(metric.PositiveDetected).Append('|')
                    .Append(metric.Precision?.ToString(CultureInfo.InvariantCulture) ?? "NA").Append('|')
                    .Append(metric.Recall?.ToString(CultureInfo.InvariantCulture) ?? "NA").Append('|')
                    .Append(metric.Lift?.ToString(CultureInfo.InvariantCulture) ?? "NA").Append('|')
                    .Append(metric.DetectedAbsoluteVarianceBaseQty?.ToString(CultureInfo.InvariantCulture) ?? "NA").Append('|')
                    .Append(metric.DetectedQuantityUomCode ?? "NA").Append('|')
                    .Append(metric.QuantityMetricStatusCode).Append('|')
                    .Append(metric.InspectionPerDetectedVariance?.ToString(CultureInfo.InvariantCulture) ?? "NA").Append('|')
                    .Append(metric.EstimatedEffortMinutes?.ToString(CultureInfo.InvariantCulture) ?? "NA")
                    .Append('\n');
            }
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString())));
    }

    private sealed record RankedRow(InventoryRiskDatasetRow Row, decimal? Score);
}
