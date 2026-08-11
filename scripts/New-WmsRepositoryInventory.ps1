[CmdletBinding()]
param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot),
    [string]$OutputDirectory = "artifacts/full-audit"
)

$ErrorActionPreference = "Stop"

$rootPath = (Resolve-Path -LiteralPath $Root).Path
$rootPrefix = $rootPath.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$outputPath = Join-Path $rootPath $OutputDirectory
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

$binaryExtensions = @(
    ".7z", ".bmp", ".dll", ".doc", ".docx", ".eot", ".exe", ".gif",
    ".ico", ".jpeg", ".jpg", ".otf", ".pdf", ".pdb", ".png", ".so",
    ".ttf", ".webp", ".woff", ".woff2", ".xls", ".xlsx", ".zip"
)

$sourceExtensions = @(
    ".cs", ".cshtml", ".css", ".html", ".js", ".json", ".md", ".ps1",
    ".razor", ".scss", ".sql", ".ts", ".txt", ".xml", ".yml", ".yaml"
)

function Get-InventoryClassification {
    param(
        [Parameter(Mandatory)] [string]$RelativePath,
        [Parameter(Mandatory)] [AllowEmptyString()] [string]$Extension
    )

    $path = $RelativePath.Replace("\", "/")
    $lower = $path.ToLowerInvariant()
    $rootName = [IO.Path]::GetFileName($path)

    if ($lower -match "(^|/)(bin|obj)(/|$)") {
        return @("generated", "No", "Build output; inspect manifest/freshness only.")
    }

    if ($lower -match "(^|/)(node_modules|wwwroot/lib|wwwroot/vendor)(/|$)") {
        return @("vendor", "No", "Third-party dependency; inspect lockfile, license and vulnerability metadata.")
    }

    if ($lower -match "(^|/)(artifacts|logs|test-results|testresults|playwright-report)(/|$)" -or
        $lower -match "(^|/)app_data/(uploads|dataprotection-keys)(/|$)" -or
        $lower -match "(^|/)tests/visual/.auth(/|$)") {
        return @("artifact", "No", "Runtime/test artifact; inspect freshness, retention and sensitive-data exposure.")
    }

    if ($path -notmatch "/" -and $rootName -match "^(FINAL_|WMS_).*(REPORT|ASSESSMENT).*\.md$") {
        return @("artifact-report", "No", "Historical report; lead only, never current evidence without rerun.")
    }

    if ($lower -match "^migrations/.*\.designer\.cs$") {
        return @("generated-migration", "No", "EF generated migration metadata; validate through model/migration checks.")
    }

    if ($lower -match "^migrations/" -or $lower -match "^scripts/.*\.sql$") {
        return @("schema-migration", "Yes", "")
    }

    if ($lower -match "(^|/)(wms\.tests|tests)(/|$)") {
        return @("test", "Yes", "")
    }

    if ($lower -match "^views/" -or
        ($lower -match "^wwwroot/" -and $Extension -in @(".css", ".js", ".html"))) {
        return @("first-party-ui", "Yes", "")
    }

    if ($lower -match "^docs/" -or $lower -match "^tai_lieu_onboarding_wms/" -or
        $rootName -in @("README.md", "ROADMAP_WMS_ENTERPRISE_100_PERCENT_FULL.md", "PROMPT_AUDIT_FIX_WMS_ENTERPRISE_100_PERCENT.md")) {
        return @("governance-doc", "Yes", "")
    }

    if ($Extension -in $binaryExtensions) {
        return @("binary-asset", "No", "Binary asset; inspect metadata, license, references and rendered result.")
    }

    if ($rootName -match "^(appsettings.*\.json|package(-lock)?\.json|.*\.csproj|.*\.slnx?|.*\.props|.*\.targets)$" -or
        $lower -match "^properties/.*\.json$") {
        return @("config", "Yes", "")
    }

    if ($Extension -in $sourceExtensions) {
        return @("first-party-source", "Yes", "")
    }

    return @("first-party-other", "Yes", "Unknown first-party type; manual classification required.")
}

$files = Get-ChildItem -LiteralPath $rootPath -Recurse -Force -File |
    Sort-Object FullName

$rows = foreach ($file in $files) {
    if (-not $file.FullName.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Inventory path is outside repository root: $($file.FullName)"
    }

    $relative = $file.FullName.Substring($rootPrefix.Length).Replace("\", "/")
    $extension = $file.Extension.ToLowerInvariant()
    $classification = Get-InventoryClassification -RelativePath $relative -Extension $extension
    $category = $classification[0]
    $lineReview = $classification[1]
    $excludeReason = $classification[2]
    $hash = ""

    if ($lineReview -eq "Yes") {
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName).Hash
    }

    [pscustomobject]@{
        Path = $relative
        Category = $category
        FirstParty = if ($category -like "first-party*" -or $category -in @("config", "schema-migration", "test", "governance-doc")) { "Yes" } else { "No" }
        LineReviewRequired = $lineReview
        ReviewStatus = if ($lineReview -eq "Yes") { "UNKNOWN" } else { "EXCLUDED" }
        Reviewer = ""
        FindingIds = ""
        TestEvidence = ""
        ExcludeReason = $excludeReason
        Bytes = $file.Length
        LastWriteTimeUtc = $file.LastWriteTimeUtc.ToString("o")
        SHA256 = $hash
    }
}

$manifestPath = Join-Path $outputPath "FILE_AUDIT_MANIFEST.csv"
$rows | Export-Csv -LiteralPath $manifestPath -NoTypeInformation -Encoding utf8

$categoryRows = $rows |
    Group-Object Category |
    Sort-Object Name |
    ForEach-Object {
        [pscustomobject]@{
            Category = $_.Name
            Files = $_.Count
            Bytes = ($_.Group | Measure-Object Bytes -Sum).Sum
            ReviewRequired = ($_.Group | Where-Object LineReviewRequired -eq "Yes").Count
        }
    }

$topLevelRows = $rows |
    Group-Object { if ($_.Path.Contains("/")) { $_.Path.Split("/")[0] } else { "(root)" } } |
    Sort-Object Name |
    ForEach-Object {
        [pscustomobject]@{
            Root = $_.Name
            Files = $_.Count
            Bytes = ($_.Group | Measure-Object Bytes -Sum).Sum
        }
    }

$generatedAt = [DateTimeOffset]::UtcNow.ToString("o")
$summary = @(
    "# Repository Inventory",
    "",
    "- Generated UTC: $generatedAt",
    "- Repository root: ``$rootPath``",
    "- Total files: $($rows.Count)",
    "- Review-required files: $(($rows | Where-Object LineReviewRequired -eq 'Yes').Count)",
    "- Excluded from line review: $(($rows | Where-Object LineReviewRequired -eq 'No').Count)",
    "- Manifest: ``artifacts/full-audit/FILE_AUDIT_MANIFEST.csv``",
    "",
    "## Category Summary",
    "",
    "| Category | Files | Bytes | Review required |",
    "|---|---:|---:|---:|"
)

foreach ($row in $categoryRows) {
    $summary += "| $($row.Category) | $($row.Files) | $($row.Bytes) | $($row.ReviewRequired) |"
}

$summary += @(
    "",
    "## Top-Level Summary",
    "",
    "| Root | Files | Bytes |",
    "|---|---:|---:|"
)

foreach ($row in $topLevelRows) {
    $summary += "| $($row.Root) | $($row.Files) | $($row.Bytes) |"
}

$summary += @(
    "",
    "## Review Contract",
    "",
    "- Every path is present in the manifest or the inventory command must fail review.",
    "- Generated, vendor, artifact and binary files are not line-reviewed, but their freshness, origin, license, references and sensitive-data risk remain in scope.",
    "- ``UNKNOWN`` is not evidence of pass. First-party rows are updated only after runtime trace, review or test evidence.",
    "- Historical reports are leads only and must not be used as current evidence without rerunning the referenced command/test.",
    "- No file content from appsettings or other secret-bearing configuration is copied into these artifacts."
)

$summaryPath = Join-Path $outputPath "REPOSITORY_INVENTORY.md"
[IO.File]::WriteAllLines($summaryPath, $summary, [Text.UTF8Encoding]::new($false))

Write-Output "Inventory manifest: $manifestPath"
Write-Output "Inventory summary:  $summaryPath"
Write-Output "Files inventoried:   $($rows.Count)"
