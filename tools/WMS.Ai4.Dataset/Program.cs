using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using WMS.Data;
using WMS.Services;

return await Ai4DatasetCommand.RunAsync(args);

internal static class Ai4DatasetCommand
{
    private const int Success = 0;
    private const int InvalidArguments = 2;
    private const int BlockedEnvironment = 3;
    private const int UnexpectedFailure = 4;
    private const int DataNotReady = 5;

    private static readonly string[] SourceFiles =
    {
        "Data/AppDbContext.cs",
        "Models/InventoryRiskModels.cs",
        "Models/InventoryTransaction.cs",
        "Models/StockCountLine.cs",
        "Models/StockCountSheet.cs",
        "Models/Voucher.cs",
        "Models/VoucherDetail.cs",
        "Services/InventoryRiskDatasetService.cs",
        "Services/InventoryRiskTemporalSplitService.cs",
        "Services/InventoryRiskBenchmarkService.cs",
        "Services/InventoryRiskExperimentArtifactWriter.cs",
        "tools/WMS.Ai4.Dataset/Program.cs",
        "tools/WMS.Ai4.Dataset/WMS.Ai4.Dataset.csproj",
        "tools/WMS.Ai4.Dataset/packages.lock.json",
        "global.json",
        "WMS.csproj",
        "WMS.sln",
        "packages.lock.json"
    };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Any(argument => argument is "--help" or "-h"))
        {
            PrintUsage();
            return Success;
        }

        if (!CommandOptions.TryParse(args, out var options, out var argumentError))
        {
            Console.Error.WriteLine($"AI4_ARGUMENT_ERROR: {argumentError}");
            PrintUsage();
            return InvalidArguments;
        }

        var repositoryRoot = FindRepositoryRoot();
        if (repositoryRoot == null)
        {
            Console.Error.WriteLine("AI4_ENVIRONMENT_BLOCKED: repository root was not found.");
            return BlockedEnvironment;
        }

        var query = new InventoryRiskDatasetQuery
        {
            BuildAsOf = options.BuildAsOf,
            OutcomeHorizonDays = options.OutcomeHorizonDays,
            Seed = options.Seed,
            IncludeIsolatedTestData = options.IncludeIsolatedTestData,
            IncludeDemoData = options.IncludeDemoData,
            AllowedWarehouseIds = options.WarehouseIds,
            AllowedOwnerPartnerIds = options.OwnerPartnerIds
        };
        var sourceHashes = BuildSourceHashes(repositoryRoot);

        InventoryRiskDatasetBuildResult dataset;
        var environmentBlocked = false;
        try
        {
            var connectionString = ReadConnectionString(repositoryRoot, options.AllowApplicationConnection);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                dataset = BlockedDataset(query, "READ_ONLY_CONNECTION_CONFIGURATION_UNAVAILABLE");
                environmentBlocked = true;
            }
            else
            {
                using var readTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.ReadTimeoutSeconds));
                (dataset, environmentBlocked) = await BuildDatasetAsync(connectionString, query, readTimeout.Token);
            }
        }
        catch (OperationCanceledException)
        {
            dataset = BlockedDataset(query, "READ_TIMEOUT_OR_CANCELLED");
            environmentBlocked = true;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            dataset = BlockedDataset(query, $"CONFIGURATION_READ_FAILED_{exception.GetType().Name.ToUpperInvariant()}");
            environmentBlocked = true;
        }
        catch (Exception exception)
        {
            var stage = exception.Data["AI4_STAGE"] as string ?? "UNCLASSIFIED";
            Console.Error.WriteLine($"AI4_UNEXPECTED_FAILURE: {stage}:{exception.GetType().Name}");
            return UnexpectedFailure;
        }

        var split = BuildSplit(dataset);
        var benchmark = new InventoryRiskBenchmarkService().Evaluate(split.TestRows, options.Seed);
        var runName = $"{options.BuildAsOf:yyyyMMddTHHmmss}-{dataset.DatasetHash[..12].ToLowerInvariant()}";
        var outputDirectory = Path.Combine(
            repositoryRoot,
            "artifacts",
            "ai-smart-cycle-count",
            "AI4",
            "runs",
            runName);

        InventoryRiskExperimentArtifactResult artifacts;
        try
        {
            artifacts = await new InventoryRiskExperimentArtifactWriter().WriteAsync(
                new InventoryRiskExperimentArtifactRequest
                {
                    OutputDirectory = outputDirectory,
                    Dataset = dataset,
                    Split = split,
                    Benchmark = benchmark,
                    SourceHashes = sourceHashes
                });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"AI4_ARTIFACT_WRITE_FAILED: {exception.GetType().Name}");
            return UnexpectedFailure;
        }

        Console.WriteLine($"AI4_DATASET_STATUS={dataset.Status}");
        Console.WriteLine($"AI4_DATASET_ROWS={dataset.Rows.Count}");
        Console.WriteLine($"AI4_DATASET_POSITIVE={dataset.PositiveCount}");
        Console.WriteLine($"AI4_DATASET_NEGATIVE={dataset.NegativeCount}");
        Console.WriteLine($"AI4_DATASET_HASH={dataset.DatasetHash}");
        Console.WriteLine($"AI4_SPLIT_STATUS={split.Status}");
        Console.WriteLine($"AI4_SPLIT_HASH={split.SplitHash}");
        Console.WriteLine($"AI4_BENCHMARK_STATUS={benchmark.Status}");
        Console.WriteLine($"AI4_BENCHMARK_HASH={benchmark.BenchmarkHash}");
        Console.WriteLine($"AI4_ARTIFACT_COUNT={artifacts.ArtifactHashes.Count + 1}");
        Console.WriteLine($"AI4_ARTIFACT_DIRECTORY={Path.GetRelativePath(repositoryRoot, outputDirectory).Replace('\\', '/')}");

        if (environmentBlocked)
            return BlockedEnvironment;
        if (options.RequireDataReady
            && (dataset.Status != InventoryRiskExperimentStatus.Ready
                || split.Status != InventoryRiskExperimentStatus.Ready))
        {
            return DataNotReady;
        }
        return Success;
    }

    private static async Task<(InventoryRiskDatasetBuildResult Result, bool EnvironmentBlocked)> BuildDatasetAsync(
        string connectionString,
        InventoryRiskDatasetQuery query,
        CancellationToken cancellationToken)
    {
        var stage = "CONFIGURE_CONTEXT";
        try
        {
            stage = "NORMALIZE_CONNECTION_STRING";
            connectionString = new SqlConnectionStringBuilder(connectionString)
            {
                MultipleActiveResultSets = false,
                ConnectRetryCount = 1,
                ConnectRetryInterval = 1
            }.ConnectionString;
            var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(connectionString, sql => sql.CommandTimeout(120))
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
                .Options;
            await using var db = new AppDbContext(dbOptions) { SkipAudit = true };
            stage = "OPEN_CONNECTION";
            await db.Database.OpenConnectionAsync(cancellationToken);

            stage = "SCHEMA_PRECHECK";
            if (!await HasRequiredSchemaAsync(db, cancellationToken))
                return (BlockedDataset(query, "REQUIRED_AI4_SCHEMA_OR_COLUMNS_UNAVAILABLE"), true);
            stage = "SNAPSHOT_ISOLATION_PRECHECK";
            if (!await HasSnapshotIsolationAsync(db, cancellationToken))
                return (BlockedDataset(query, "SQL_SNAPSHOT_ISOLATION_DISABLED"), true);

            stage = "BEGIN_SNAPSHOT_TRANSACTION";
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Snapshot,
                cancellationToken);
            stage = "BUILD_DATASET";
            var result = await new InventoryRiskDatasetService(db).BuildAsync(query, cancellationToken);
            stage = "READ_ONLY_GUARD";
            if (db.ChangeTracker.Entries().Any(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
                throw new InvalidOperationException("READ_ONLY_GUARD_FAILED");
            stage = "ROLLBACK_READ_TRANSACTION";
            await transaction.RollbackAsync(cancellationToken);
            return (result, false);
        }
        catch (SqlException exception)
        {
            return (BlockedDataset(query, $"SQL_READ_FAILED_{exception.Number}"), true);
        }
        catch (TimeoutException)
        {
            return (BlockedDataset(query, "SQL_READ_TIMEOUT"), true);
        }
        catch (ArgumentException) when (stage == "NORMALIZE_CONNECTION_STRING")
        {
            return (BlockedDataset(query, "SQL_CONNECTION_FORMAT_INVALID"), true);
        }
        catch (InvalidOperationException) when (stage == "OPEN_CONNECTION")
        {
            return (BlockedDataset(query, "SQL_CONNECTION_OPEN_INVALID_OPERATION"), true);
        }
        catch (Exception exception)
        {
            exception.Data["AI4_STAGE"] = stage;
            throw;
        }
    }

    private static InventoryRiskTemporalSplitResult BuildSplit(InventoryRiskDatasetBuildResult dataset)
    {
        var service = new InventoryRiskTemporalSplitService();
        var configuration = service.CreateDefaultConfiguration(dataset.Rows, dataset.Query.OutcomeHorizonDays);
        if (configuration != null)
            return service.Split(dataset.Rows, configuration);

        const string code = "TEMPORAL_SPLIT_INSUFFICIENT_HISTORY";
        return new InventoryRiskTemporalSplitResult
        {
            Status = InventoryRiskExperimentStatus.BlockedData,
            ReadinessCodes = new[] { code },
            SplitHash = Hash($"{dataset.DatasetHash}|{code}|{dataset.Query.OutcomeHorizonDays}")
        };
    }

    private static InventoryRiskDatasetBuildResult BlockedDataset(
        InventoryRiskDatasetQuery query,
        string readinessCode)
        => new()
        {
            Status = readinessCode.Contains("INSUFFICIENT", StringComparison.Ordinal)
                ? InventoryRiskExperimentStatus.BlockedData
                : InventoryRiskExperimentStatus.BlockedConfiguration,
            Query = query,
            ReadinessCodes = new[] { readinessCode },
            DatasetHash = Hash(
                $"{query.DatasetSchemaVersion}|{query.FeatureSchemaVersion}|{query.BuildAsOf:O}|" +
                $"{query.OutcomeHorizonDays}|{query.Seed}|{query.IncludeIsolatedTestData}|{query.IncludeDemoData}|" +
                $"{string.Join(',', query.AllowedWarehouseIds.Order())}|" +
                $"{string.Join(',', query.AllowedOwnerPartnerIds.Order())}|{readinessCode}")
        };

    private static async Task<bool> HasRequiredSchemaAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            WITH RequiredColumns AS
            (
                SELECT *
                FROM (VALUES
                    (N'dbo', N'InventoryRiskModelVersions', N'InventoryRiskModelVersionId'),
                    (N'dbo', N'InventoryRiskModelVersions', N'Version'),
                    (N'dbo', N'InventoryRiskModelVersions', N'FeatureSchemaVersion'),
                    (N'dbo', N'InventoryRiskFeatureSnapshots', N'InventoryRiskFeatureSnapshotId'),
                    (N'dbo', N'InventoryRiskFeatureSnapshots', N'PredictionCutoff'),
                    (N'dbo', N'InventoryRiskFeatureSnapshots', N'FeatureJson'),
                    (N'dbo', N'InventoryRiskFeatureSnapshots', N'FeatureHash'),
                    (N'dbo', N'InventoryRiskFeatureSnapshots', N'SourceWatermark'),
                    (N'dbo', N'InventoryRiskFeatureSnapshots', N'CreatedAt'),
                    (N'dbo', N'InventoryRiskPredictions', N'InventoryRiskFeatureSnapshotId'),
                    (N'dbo', N'InventoryRiskPredictions', N'RiskScore'),
                    (N'dbo', N'CycleCountRecommendations', N'InventoryRiskPredictionId'),
                    (N'dbo', N'CycleCountRecommendations', N'StockCountSheetId'),
                    (N'dbo', N'StockCountSheets', N'StockCountSheetId'),
                    (N'dbo', N'StockCountSheets', N'Status'),
                    (N'dbo', N'StockCountSheets', N'CompletedAt'),
                    (N'dbo', N'StockCountSheets', N'ApprovedAt'),
                    (N'dbo', N'StockCountSheets', N'GeneratedAdjustmentVoucherId'),
                    (N'dbo', N'StockCountLines', N'StockCountLineId'),
                    (N'dbo', N'StockCountLines', N'CountedQty'),
                    (N'dbo', N'StockCountLines', N'Variance'),
                    (N'dbo', N'StockCountLines', N'CountedAt'),
                    (N'dbo', N'Vouchers', N'VoucherType'),
                    (N'dbo', N'Vouchers', N'IsPosted'),
                    (N'dbo', N'Vouchers', N'IsCancelled'),
                    (N'dbo', N'VoucherDetails', N'BaseQty'),
                    (N'dbo', N'VoucherDetails', N'LocationId'),
                    (N'dbo', N'InventoryTransactions', N'TransactionType'),
                    (N'dbo', N'InventoryTransactions', N'ReferenceType'),
                    (N'dbo', N'InventoryTransactions', N'ReferenceId'),
                    (N'dbo', N'InventoryTransactions', N'QuantityDelta'),
                    (N'dbo', N'Items', N'BaseUomId'),
                    (N'dbo', N'Items', N'TrackSerial'),
                    (N'dbo', N'UnitsOfMeasure', N'UomCode'),
                    (N'dbo', N'Locations', N'WarehouseId')
                ) AS columns([SchemaName], [TableName], [ColumnName])
            )
            SELECT COUNT_BIG(*)
            FROM RequiredColumns AS required
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM sys.schemas AS schemas
                INNER JOIN sys.tables AS tables ON tables.[schema_id] = schemas.[schema_id]
                INNER JOIN sys.columns AS columns ON columns.[object_id] = tables.[object_id]
                WHERE schemas.[name] = required.[SchemaName]
                  AND tables.[name] = required.[TableName]
                  AND columns.[name] = required.[ColumnName]
            );
            """;
        var missingCount = Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
        return missingCount == 0;
    }

    private static async Task<bool> HasSnapshotIsolationAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT CAST([snapshot_isolation_state] AS int)
            FROM sys.databases
            WHERE [database_id] = DB_ID();
            """;
        var state = Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
        return state == 1;
    }

    private static string? ReadConnectionString(string repositoryRoot, bool allowApplicationConnection)
    {
        var readOnlyValue = Environment.GetEnvironmentVariable("ConnectionStrings__Ai4ReadOnly");
        if (!string.IsNullOrWhiteSpace(readOnlyValue))
            return readOnlyValue;
        if (!allowApplicationConnection)
            return null;

        var environmentValue = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (!string.IsNullOrWhiteSpace(environmentValue))
            return environmentValue;

        var appSettingsPath = Path.Combine(repositoryRoot, "appsettings.json");
        using var document = JsonDocument.Parse(File.ReadAllText(appSettingsPath));
        return document.RootElement.TryGetProperty("ConnectionStrings", out var connectionStrings)
            && connectionStrings.TryGetProperty("DefaultConnection", out var defaultConnection)
                ? defaultConnection.GetString()
                : null;
    }

    private static IReadOnlyDictionary<string, string> BuildSourceHashes(string repositoryRoot)
    {
        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var relativePath in SourceFiles)
        {
            var fullPath = Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            hashes[relativePath] = File.Exists(fullPath)
                ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fullPath)))
                : "MISSING";
        }

        var executingAssembly = typeof(Ai4DatasetCommand).Assembly.Location;
        if (!string.IsNullOrWhiteSpace(executingAssembly) && File.Exists(executingAssembly))
        {
            hashes["executing-tool-binary"] = Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(executingAssembly)));
        }
        return hashes;
    }

    private static string? FindRepositoryRoot()
    {
        foreach (var startingPath in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(startingPath);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "WMS.sln"))
                    && File.Exists(Path.Combine(directory.FullName, "WMS.csproj")))
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
        }
        return null;
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static void PrintUsage()
    {
        Console.Error.WriteLine(
            "Usage: dotnet run --project tools/WMS.Ai4.Dataset -- " +
            "--as-of yyyy-MM-ddTHH:mm:ss (--all-scopes | --warehouse-id ID [--warehouse-id ID ...]) " +
            "[--owner-id ID ...] [--outcome-horizon-days N] [--seed N] " +
            "[--include-isolated-test-data] [--include-demo-data] [--require-data-ready] " +
            "[--allow-application-connection] [--read-timeout-seconds N]");
    }

    private sealed class CommandOptions
    {
        public DateTime BuildAsOf { get; init; }
        public int OutcomeHorizonDays { get; init; } = 90;
        public int Seed { get; init; } = 20260716;
        public bool IncludeIsolatedTestData { get; init; }
        public bool IncludeDemoData { get; init; }
        public bool RequireDataReady { get; init; }
        public bool AllowApplicationConnection { get; init; }
        public bool AllScopes { get; init; }
        public int ReadTimeoutSeconds { get; init; } = 180;
        public IReadOnlyList<int> WarehouseIds { get; init; } = Array.Empty<int>();
        public IReadOnlyList<int> OwnerPartnerIds { get; init; } = Array.Empty<int>();

        public static bool TryParse(string[] args, out CommandOptions options, out string error)
        {
            DateTime? buildAsOf = null;
            var horizon = 90;
            var seed = 20260716;
            var readTimeoutSeconds = 180;
            var includeTest = false;
            var includeDemo = false;
            var requireReady = false;
            var allowApplicationConnection = false;
            var allScopes = false;
            var warehouseIds = new List<int>();
            var ownerIds = new List<int>();

            for (var index = 0; index < args.Length; index++)
            {
                var argument = args[index];
                switch (argument)
                {
                    case "--as-of":
                        if (!TryNext(args, ref index, out var asOfText)
                            || !TryParseUnspecifiedDateTime(asOfText, out var parsedAsOf))
                        {
                            return Fail(out options, out error, "--as-of must use yyyy-MM-dd or yyyy-MM-ddTHH:mm:ss without a timezone.");
                        }
                        buildAsOf = parsedAsOf;
                        break;
                    case "--outcome-horizon-days":
                        if (!TryNext(args, ref index, out var horizonText)
                            || !int.TryParse(horizonText, NumberStyles.None, CultureInfo.InvariantCulture, out horizon)
                            || horizon is < 1 or > 3650)
                        {
                            return Fail(out options, out error, "--outcome-horizon-days must be between 1 and 3650.");
                        }
                        break;
                    case "--seed":
                        if (!TryNext(args, ref index, out var seedText)
                            || !int.TryParse(seedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out seed))
                        {
                            return Fail(out options, out error, "--seed must be a 32-bit integer.");
                        }
                        break;
                    case "--read-timeout-seconds":
                        if (!TryNext(args, ref index, out var timeoutText)
                            || !int.TryParse(timeoutText, NumberStyles.None, CultureInfo.InvariantCulture, out readTimeoutSeconds)
                            || readTimeoutSeconds is < 30 or > 1800)
                        {
                            return Fail(out options, out error, "--read-timeout-seconds must be between 30 and 1800.");
                        }
                        break;
                    case "--warehouse-id":
                        if (!TryReadPositiveId(args, ref index, warehouseIds))
                            return Fail(out options, out error, "--warehouse-id must be a positive integer.");
                        break;
                    case "--owner-id":
                        if (!TryReadPositiveId(args, ref index, ownerIds))
                            return Fail(out options, out error, "--owner-id must be a positive integer.");
                        break;
                    case "--all-scopes":
                        allScopes = true;
                        break;
                    case "--include-isolated-test-data":
                        includeTest = true;
                        break;
                    case "--include-demo-data":
                        includeDemo = true;
                        break;
                    case "--require-data-ready":
                        requireReady = true;
                        break;
                    case "--allow-application-connection":
                        allowApplicationConnection = true;
                        break;
                    case "--scope-from-secure-input":
                        return Fail(
                            out options,
                            out error,
                            "--scope-from-secure-input is an artifact placeholder; provide explicit --warehouse-id/--owner-id values at runtime.");
                    default:
                        return Fail(out options, out error, $"Unknown argument: {argument}");
                }
            }

            if (!buildAsOf.HasValue)
                return Fail(out options, out error, "--as-of is required for reproducibility.");
            if (allScopes && (warehouseIds.Count > 0 || ownerIds.Count > 0))
                return Fail(out options, out error, "--all-scopes cannot be combined with warehouse or owner scope IDs.");
            if (!allScopes && warehouseIds.Count == 0)
                return Fail(out options, out error, "Declare --all-scopes explicitly or provide at least one --warehouse-id.");

            options = new CommandOptions
            {
                BuildAsOf = buildAsOf.Value,
                OutcomeHorizonDays = horizon,
                Seed = seed,
                ReadTimeoutSeconds = readTimeoutSeconds,
                IncludeIsolatedTestData = includeTest,
                IncludeDemoData = includeDemo,
                RequireDataReady = requireReady,
                AllowApplicationConnection = allowApplicationConnection,
                AllScopes = allScopes,
                WarehouseIds = warehouseIds.Distinct().Order().ToArray(),
                OwnerPartnerIds = ownerIds.Distinct().Order().ToArray()
            };
            error = "";
            return true;
        }

        private static bool Fail(out CommandOptions options, out string error, string message)
        {
            options = new CommandOptions();
            error = message;
            return false;
        }

        private static bool TryReadPositiveId(string[] args, ref int index, ICollection<int> target)
        {
            if (!TryNext(args, ref index, out var text)
                || !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                || id <= 0)
            {
                return false;
            }
            target.Add(id);
            return true;
        }

        private static bool TryNext(string[] args, ref int index, out string value)
        {
            if (index + 1 >= args.Length)
            {
                value = "";
                return false;
            }
            value = args[++index];
            return true;
        }

        private static bool TryParseUnspecifiedDateTime(string value, out DateTime result)
        {
            var formats = new[]
            {
                "yyyy-MM-dd",
                "yyyy-MM-dd'T'HH:mm:ss",
                "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF"
            };
            if (!DateTime.TryParseExact(
                    value,
                    formats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out result))
            {
                return false;
            }
            result = DateTime.SpecifyKind(result, DateTimeKind.Unspecified);
            return true;
        }
    }
}
