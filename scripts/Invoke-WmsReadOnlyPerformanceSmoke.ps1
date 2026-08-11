param(
    [Parameter(Mandatory = $true)]
    [string]$BaseUrl,

    [Parameter(Mandatory = $true)]
    [string]$StorageStatePath,

    [ValidateRange(1, 100)]
    [int]$Iterations = 5,

    [ValidateRange(1, 16)]
    [int]$Concurrency = 2,

    [ValidateRange(5, 120)]
    [int]$RequestTimeoutSeconds = 30,

    [string]$OutputDirectory = "artifacts/performance"
)

$ErrorActionPreference = "Stop"
$baseUri = [Uri]$BaseUrl
if (-not $baseUri.IsLoopback) {
    throw "This smoke script is GET-only and intentionally restricted to a loopback WMS instance."
}

$resolvedState = Resolve-Path -LiteralPath $StorageStatePath
$state = Get-Content -LiteralPath $resolvedState -Raw -Encoding UTF8 | ConvertFrom-Json
$cookieHeader = ($state.cookies | ForEach-Object { "{0}={1}" -f $_.name, $_.value }) -join "; "
if ([string]::IsNullOrWhiteSpace($cookieHeader)) {
    throw "The Playwright storage state does not contain an authenticated cookie."
}

$routes = @(
    "/",
    "/Reports/Inventory?page=1&pageSize=25",
    "/Reports/StockMovement?page=1&pageSize=50",
    "/Reports/InventoryTransactions?page=1&pageSize=50",
    "/Reports/WarehouseOverview",
    "/Warehouses/InventoryMap"
)

if (-not ("WmsReadOnlySmokeRunner" -as [type])) {
    Add-Type -ReferencedAssemblies @("System.Net.Http.dll") -TypeDefinition @"
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public sealed class WmsSmokeSample
{
    public string Route { get; set; }
    public bool Cold { get; set; }
    public long DurationMs { get; set; }
    public int StatusCode { get; set; }
    public string FinalPath { get; set; }
    public string Error { get; set; }
    public bool Passed { get; set; }
}

public static class WmsReadOnlySmokeRunner
{
    public static async Task<List<WmsSmokeSample>> RunAsync(
        Uri baseUri,
        string cookieHeader,
        string[] routes,
        int iterations,
        int concurrency,
        int timeoutSeconds)
    {
        using (var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false
        })
        using (var client = new HttpClient(handler) { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(timeoutSeconds) })
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", cookieHeader);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Audit-Purpose", "AUDIT_TEST_GATE6_READ_ONLY");
            var samples = new ConcurrentBag<WmsSmokeSample>();

            foreach (var route in routes)
                samples.Add(await ExecuteAsync(client, route, true).ConfigureAwait(false));

            using (var gate = new SemaphoreSlim(concurrency, concurrency))
            {
                var tasks = new List<Task>();
                for (var iteration = 0; iteration < iterations; iteration++)
                {
                    foreach (var route in routes)
                    {
                        tasks.Add(Task.Run(async () =>
                        {
                            await gate.WaitAsync().ConfigureAwait(false);
                            try
                            {
                                samples.Add(await ExecuteAsync(client, route, false).ConfigureAwait(false));
                            }
                            finally
                            {
                                gate.Release();
                            }
                        }));
                    }
                }

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }

            return samples.OrderBy(x => x.Cold ? 0 : 1).ThenBy(x => x.Route).ToList();
        }
    }

    private static async Task<WmsSmokeSample> ExecuteAsync(HttpClient client, string route, bool cold)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var requestUri = route;
            for (var redirect = 0; redirect <= 5; redirect++)
            {
                using (var response = await client.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    var responseUri = response.RequestMessage != null ? response.RequestMessage.RequestUri : null;
                    var finalPath = responseUri != null ? responseUri.AbsolutePath : string.Empty;
                    var statusCode = (int)response.StatusCode;
                    var location = response.Headers.Location;
                    if (statusCode >= 300 && statusCode < 400 && location != null)
                    {
                        var nextUri = location.IsAbsoluteUri ? location : new Uri(responseUri ?? client.BaseAddress, location);
                        if (nextUri == null || client.BaseAddress == null
                            || !string.Equals(nextUri.Authority, client.BaseAddress.Authority, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Cross-origin redirect was rejected.");
                        requestUri = nextUri.PathAndQuery;
                        continue;
                    }

                    stopwatch.Stop();
                    var passed = statusCode >= 200 && statusCode < 400
                        && !finalPath.StartsWith("/Account/Login", StringComparison.OrdinalIgnoreCase);
                    return new WmsSmokeSample
                    {
                        Route = route,
                        Cold = cold,
                        DurationMs = stopwatch.ElapsedMilliseconds,
                        StatusCode = statusCode,
                        FinalPath = finalPath,
                        Error = passed ? null : "Unexpected status or authentication redirect.",
                        Passed = passed
                    };
                }
            }

            throw new InvalidOperationException("Redirect limit exceeded.");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new WmsSmokeSample
            {
                Route = route,
                Cold = cold,
                DurationMs = stopwatch.ElapsedMilliseconds,
                StatusCode = 0,
                FinalPath = string.Empty,
                Error = ex.GetType().Name,
                Passed = false
            };
        }
    }
}
"@
}

function Get-Percentile([long[]]$Values, [double]$Percentile) {
    if ($Values.Count -eq 0) { return 0 }
    $sorted = @($Values | Sort-Object)
    $index = [Math]::Max(0, [Math]::Ceiling($Percentile * $sorted.Count) - 1)
    return [long]$sorted[$index]
}

function Get-Metrics($Samples) {
    $values = @($Samples | ForEach-Object { [long]$_.DurationMs })
    $errors = @($Samples | Where-Object { -not $_.Passed }).Count
    [ordered]@{
        Requests = $values.Count
        Errors = $errors
        ErrorRatePct = if ($values.Count -eq 0) { 100 } else { [Math]::Round(($errors * 100.0) / $values.Count, 3) }
        P50Ms = Get-Percentile $values 0.50
        P95Ms = Get-Percentile $values 0.95
        P99Ms = Get-Percentile $values 0.99
        MaxMs = if ($values.Count -eq 0) { 0 } else { ($values | Measure-Object -Maximum).Maximum }
    }
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$startedAt = [DateTimeOffset]::Now
$wallClock = [Diagnostics.Stopwatch]::StartNew()
$samples = [WmsReadOnlySmokeRunner]::RunAsync(
    $baseUri,
    $cookieHeader,
    $routes,
    $Iterations,
    $Concurrency,
    $RequestTimeoutSeconds).GetAwaiter().GetResult()
$wallClock.Stop()

$coldSamples = @($samples | Where-Object Cold)
$warmSamples = @($samples | Where-Object { -not $_.Cold })
$routeMetrics = @()
foreach ($route in $routes) {
    $routeMetrics += [ordered]@{
        Route = $route
        Cold = Get-Metrics @($coldSamples | Where-Object Route -eq $route)
        Warm = Get-Metrics @($warmSamples | Where-Object Route -eq $route)
    }
}

$summary = [ordered]@{
    AuditId = "AUDIT_TEST_GATE6_READ_ONLY"
    StartedAt = $startedAt.ToString("o")
    BaseUrl = $baseUri.GetLeftPart([UriPartial]::Authority)
    Method = "GET only"
    Iterations = $Iterations
    Concurrency = $Concurrency
    WallClockMs = $wallClock.ElapsedMilliseconds
    ThroughputRequestsPerSecond = [Math]::Round(($warmSamples.Count * 1000.0) / [Math]::Max(1, $wallClock.ElapsedMilliseconds), 3)
    Cold = Get-Metrics $coldSamples
    Warm = Get-Metrics $warmSamples
    Routes = $routeMetrics
    Samples = $samples
}

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$jsonPath = Join-Path $OutputDirectory "gate6-readonly-smoke-$stamp.json"
$markdownPath = Join-Path $OutputDirectory "gate6-readonly-smoke-$stamp.md"
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$markdown = @(
    "# Gate 6 Read-only Performance Smoke",
    "",
    "- Audit ID: AUDIT_TEST_GATE6_READ_ONLY",
    "- Target: loopback only",
    "- Method: GET only",
    "- Iterations: $Iterations",
    "- Concurrency: $Concurrency",
    "- Warm throughput: $($summary.ThroughputRequestsPerSecond) requests/second",
    "- Warm error rate: $($summary.Warm.ErrorRatePct)%",
    "",
    "| Route | Cold ms | Warm p50 | Warm p95 | Warm p99 | Errors |",
    "|---|---:|---:|---:|---:|---:|"
)
foreach ($row in $routeMetrics) {
    $markdown += "| $($row.Route) | $($row.Cold.P50Ms) | $($row.Warm.P50Ms) | $($row.Warm.P95Ms) | $($row.Warm.P99Ms) | $($row.Warm.Errors) |"
}
$markdown | Set-Content -LiteralPath $markdownPath -Encoding UTF8

Write-Output "JSON=$jsonPath"
Write-Output "MARKDOWN=$markdownPath"
Write-Output "WARM_ERROR_RATE_PCT=$($summary.Warm.ErrorRatePct)"
Write-Output "WARM_P95_MS=$($summary.Warm.P95Ms)"

if ($summary.Warm.Errors -gt 0) { exit 2 }
