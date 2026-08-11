using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace WMS.Services;

public enum InventoryRiskDatasetPartition
{
    Train = 1,
    Validation = 2,
    Test = 3
}

public sealed class InventoryRiskTemporalSplitConfiguration
{
    public DateTime TrainEnd { get; init; }
    public DateTime ValidationStart { get; init; }
    public DateTime ValidationEnd { get; init; }
    public DateTime TestStart { get; init; }
    public DateTime TestEnd { get; init; }
    public int OutcomeHorizonDays { get; init; } = 90;
}

public sealed class InventoryRiskTemporalSplitResult
{
    public InventoryRiskExperimentStatus Status { get; init; }
    public InventoryRiskTemporalSplitConfiguration? Configuration { get; init; }
    public IReadOnlyList<InventoryRiskDatasetRow> TrainRows { get; init; } = Array.Empty<InventoryRiskDatasetRow>();
    public IReadOnlyList<InventoryRiskDatasetRow> ValidationRows { get; init; } = Array.Empty<InventoryRiskDatasetRow>();
    public IReadOnlyList<InventoryRiskDatasetRow> TestRows { get; init; } = Array.Empty<InventoryRiskDatasetRow>();
    public IReadOnlyList<string> ReadinessCodes { get; init; } = Array.Empty<string>();
    public int EmbargoExcludedCount { get; init; }
    public int OutsideWindowCount { get; init; }
    public int PurgedEntityOverlapCount { get; init; }
    public string SplitHash { get; init; } = "";
}

public interface IInventoryRiskTemporalSplitService
{
    InventoryRiskTemporalSplitConfiguration? CreateDefaultConfiguration(
        IReadOnlyList<InventoryRiskDatasetRow> rows,
        int outcomeHorizonDays);

    InventoryRiskTemporalSplitResult Split(
        IReadOnlyList<InventoryRiskDatasetRow> rows,
        InventoryRiskTemporalSplitConfiguration configuration);
}

public sealed class InventoryRiskTemporalSplitService : IInventoryRiskTemporalSplitService
{
    public InventoryRiskTemporalSplitConfiguration? CreateDefaultConfiguration(
        IReadOnlyList<InventoryRiskDatasetRow> rows,
        int outcomeHorizonDays)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (outcomeHorizonDays < 1)
            throw new ArgumentOutOfRangeException(nameof(outcomeHorizonDays));

        var cutoffs = rows
            .Select(row => row.PredictionCutoff)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
        if (cutoffs.Length < 5)
            return null;

        var trainEnd = cutoffs[Math.Clamp((int)Math.Floor((cutoffs.Length - 1) * 0.60), 0, cutoffs.Length - 1)];
        var validationTarget = cutoffs[Math.Clamp((int)Math.Floor((cutoffs.Length - 1) * 0.80), 0, cutoffs.Length - 1)];
        var validationStart = cutoffs.FirstOrDefault(value => value >= trainEnd.AddDays(outcomeHorizonDays));
        if (validationStart == default || validationStart > validationTarget)
            return null;

        var validationEnd = validationTarget;
        var testStart = cutoffs.FirstOrDefault(value => value >= validationEnd.AddDays(outcomeHorizonDays));
        if (testStart == default || testStart > cutoffs[^1])
            return null;

        return new InventoryRiskTemporalSplitConfiguration
        {
            TrainEnd = trainEnd,
            ValidationStart = validationStart,
            ValidationEnd = validationEnd,
            TestStart = testStart,
            TestEnd = cutoffs[^1],
            OutcomeHorizonDays = outcomeHorizonDays
        };
    }

    public InventoryRiskTemporalSplitResult Split(
        IReadOnlyList<InventoryRiskDatasetRow> rows,
        InventoryRiskTemporalSplitConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(configuration);
        var configurationErrors = ValidateConfiguration(configuration);
        if (configurationErrors.Count > 0)
        {
            return new InventoryRiskTemporalSplitResult
            {
                Status = InventoryRiskExperimentStatus.BlockedConfiguration,
                Configuration = configuration,
                ReadinessCodes = configurationErrors,
                SplitHash = HashSplit(configuration, Array.Empty<InventoryRiskDatasetRow>(), Array.Empty<InventoryRiskDatasetRow>(), Array.Empty<InventoryRiskDatasetRow>())
            };
        }

        var classified = new List<ClassifiedRow>(rows.Count);
        var embargoExcluded = 0;
        var outsideWindow = 0;
        foreach (var row in rows.OrderBy(row => row.PredictionCutoff).ThenBy(row => row.SampleKey, StringComparer.Ordinal))
        {
            var partition = Classify(row.PredictionCutoff, configuration);
            if (partition.HasValue)
            {
                classified.Add(new ClassifiedRow(row, partition.Value));
            }
            else if (IsInEmbargo(row.PredictionCutoff, configuration))
            {
                embargoExcluded++;
            }
            else
            {
                outsideWindow++;
            }
        }

        var purgedEntityOverlap = 0;
        var retained = new List<ClassifiedRow>(classified.Count);
        foreach (var group in classified.GroupBy(row => row.Row.EntityKeyHash, StringComparer.Ordinal))
        {
            var latestPartition = group.Max(row => row.Partition);
            foreach (var row in group)
            {
                if (row.Partition == latestPartition)
                    retained.Add(row);
                else
                    purgedEntityOverlap++;
            }
        }

        var train = Partition(retained, InventoryRiskDatasetPartition.Train);
        var validation = Partition(retained, InventoryRiskDatasetPartition.Validation);
        var test = Partition(retained, InventoryRiskDatasetPartition.Test);
        var readinessCodes = new List<string>();
        AddClassReadiness(readinessCodes, "TRAIN", train);
        AddClassReadiness(readinessCodes, "VALIDATION", validation);
        AddClassReadiness(readinessCodes, "TEST", test);
        if (test.Count < 100)
            readinessCodes.Add("TEST_INSUFFICIENT_ROWS_FOR_PRECISION_AT_100");
        if (retained.Any(row => row.Row.OutcomeApprovedAt <= row.Row.PredictionCutoff))
            readinessCodes.Add("LABEL_AVAILABLE_AT_OR_BEFORE_PREDICTION");
        if (retained.Any(row => row.Row.OutcomeApprovedAt > row.Row.PredictionCutoff.AddDays(configuration.OutcomeHorizonDays)))
            readinessCodes.Add("LABEL_OUTSIDE_OUTCOME_HORIZON");
        if (train.Any(row => row.OutcomeApprovedAt >= configuration.ValidationStart))
            readinessCodes.Add("TRAIN_LABEL_CROSSES_VALIDATION_BOUNDARY");
        if (validation.Any(row => row.OutcomeApprovedAt >= configuration.TestStart))
            readinessCodes.Add("VALIDATION_LABEL_CROSSES_TEST_BOUNDARY");

        return new InventoryRiskTemporalSplitResult
        {
            Status = readinessCodes.Count == 0
                ? InventoryRiskExperimentStatus.Ready
                : InventoryRiskExperimentStatus.BlockedData,
            Configuration = configuration,
            TrainRows = train,
            ValidationRows = validation,
            TestRows = test,
            ReadinessCodes = readinessCodes,
            EmbargoExcludedCount = embargoExcluded,
            OutsideWindowCount = outsideWindow,
            PurgedEntityOverlapCount = purgedEntityOverlap,
            SplitHash = HashSplit(configuration, train, validation, test)
        };
    }

    private static InventoryRiskDatasetPartition? Classify(
        DateTime cutoff,
        InventoryRiskTemporalSplitConfiguration configuration)
    {
        if (cutoff <= configuration.TrainEnd)
            return InventoryRiskDatasetPartition.Train;
        if (cutoff >= configuration.ValidationStart && cutoff <= configuration.ValidationEnd)
            return InventoryRiskDatasetPartition.Validation;
        if (cutoff >= configuration.TestStart && cutoff <= configuration.TestEnd)
            return InventoryRiskDatasetPartition.Test;
        return null;
    }

    private static bool IsInEmbargo(DateTime cutoff, InventoryRiskTemporalSplitConfiguration configuration)
        => (cutoff > configuration.TrainEnd && cutoff < configuration.ValidationStart)
            || (cutoff > configuration.ValidationEnd && cutoff < configuration.TestStart);

    private static List<InventoryRiskDatasetRow> Partition(
        IEnumerable<ClassifiedRow> rows,
        InventoryRiskDatasetPartition partition)
        => rows
            .Where(row => row.Partition == partition)
            .Select(row => row.Row)
            .OrderBy(row => row.PredictionCutoff)
            .ThenBy(row => row.SampleKey, StringComparer.Ordinal)
            .ToList();

    private static List<string> ValidateConfiguration(InventoryRiskTemporalSplitConfiguration configuration)
    {
        var codes = new List<string>();
        if (configuration.OutcomeHorizonDays < 1)
            codes.Add("INVALID_OUTCOME_HORIZON");
        if (configuration.TrainEnd == default
            || configuration.ValidationStart == default
            || configuration.ValidationEnd == default
            || configuration.TestStart == default
            || configuration.TestEnd == default)
        {
            codes.Add("SPLIT_BOUNDARY_MISSING");
            return codes;
        }
        if (configuration.ValidationStart > configuration.ValidationEnd
            || configuration.TestStart > configuration.TestEnd
            || configuration.TrainEnd >= configuration.ValidationStart
            || configuration.ValidationEnd >= configuration.TestStart)
        {
            codes.Add("SPLIT_BOUNDARY_ORDER_INVALID");
        }
        if (configuration.ValidationStart < configuration.TrainEnd.AddDays(configuration.OutcomeHorizonDays))
            codes.Add("TRAIN_VALIDATION_EMBARGO_TOO_SHORT");
        if (configuration.TestStart < configuration.ValidationEnd.AddDays(configuration.OutcomeHorizonDays))
            codes.Add("VALIDATION_TEST_EMBARGO_TOO_SHORT");
        return codes;
    }

    private static void AddClassReadiness(
        ICollection<string> codes,
        string partition,
        IReadOnlyCollection<InventoryRiskDatasetRow> rows)
    {
        if (rows.Count == 0)
        {
            codes.Add($"{partition}_EMPTY");
            return;
        }
        if (!rows.Any(row => row.HasQuantityVariance))
            codes.Add($"{partition}_NO_POSITIVE_CLASS");
        if (!rows.Any(row => !row.HasQuantityVariance))
            codes.Add($"{partition}_NO_NEGATIVE_CLASS");
    }

    private static string HashSplit(
        InventoryRiskTemporalSplitConfiguration configuration,
        IReadOnlyList<InventoryRiskDatasetRow> train,
        IReadOnlyList<InventoryRiskDatasetRow> validation,
        IReadOnlyList<InventoryRiskDatasetRow> test)
    {
        var value = new StringBuilder()
            .Append(configuration.TrainEnd.ToString("O", CultureInfo.InvariantCulture)).Append('|')
            .Append(configuration.ValidationStart.ToString("O", CultureInfo.InvariantCulture)).Append('|')
            .Append(configuration.ValidationEnd.ToString("O", CultureInfo.InvariantCulture)).Append('|')
            .Append(configuration.TestStart.ToString("O", CultureInfo.InvariantCulture)).Append('|')
            .Append(configuration.TestEnd.ToString("O", CultureInfo.InvariantCulture)).Append('|')
            .Append(configuration.OutcomeHorizonDays).Append('\n');
        AppendPartition(value, "TRAIN", train);
        AppendPartition(value, "VALIDATION", validation);
        AppendPartition(value, "TEST", test);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString())));
    }

    private static void AppendPartition(StringBuilder value, string name, IEnumerable<InventoryRiskDatasetRow> rows)
    {
        foreach (var row in rows)
        {
            value.Append(name).Append('|')
                .Append(row.SampleKey).Append('|')
                .Append(row.EntityKeyHash).Append('|')
                .Append(row.PredictionCutoff.ToString("O", CultureInfo.InvariantCulture)).Append('|')
                .Append(row.OutcomeCountedAt.ToString("O", CultureInfo.InvariantCulture)).Append('|')
                .Append(row.OutcomeApprovedAt.ToString("O", CultureInfo.InvariantCulture)).Append('|')
                .Append(row.FeatureHash).Append('|')
                .Append(row.BaseUomCode).Append('|')
                .Append(row.HasQuantityVariance ? '1' : '0').Append('|')
                .Append(row.AbsoluteVarianceBaseQty.ToString(CultureInfo.InvariantCulture))
                .Append('\n');
        }
    }

    private sealed record ClassifiedRow(InventoryRiskDatasetRow Row, InventoryRiskDatasetPartition Partition);
}
