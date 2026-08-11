[CmdletBinding()]
param(
    [string]$ConnectionString = $env:WMS_DATA_QUALITY_SQL_CONNECTION_STRING,
    [ValidateSet("Environment", "Appsettings", "LaunchProfile")]
    [string]$ConnectionSource = "Environment",
    [string]$ConfigurationPath = "appsettings.json",
    [string]$LaunchSettingsPath = "Properties/launchSettings.json",
    [string]$LaunchProfile = "http",
    [string]$SqlFile = "scripts/WmsDataQualityAudit.sql",
    [string]$OutputPath = "artifacts/data-quality/wms-data-quality-audit.txt",
    [switch]$DetailedOutput,
    [switch]$ValidateSqlOnly
)

$ErrorActionPreference = "Stop"

function Resolve-ConnectionString {
    if (-not [string]::IsNullOrWhiteSpace($ConnectionString)) {
        return $ConnectionString
    }

    switch ($ConnectionSource) {
        "Appsettings" {
            if (-not (Test-Path -LiteralPath $ConfigurationPath)) {
                throw "Configuration file not found: $ConfigurationPath"
            }
            $json = Get-Content -LiteralPath $ConfigurationPath -Raw -Encoding UTF8 | ConvertFrom-Json
            return [string]$json.ConnectionStrings.DefaultConnection
        }
        "LaunchProfile" {
            if (-not (Test-Path -LiteralPath $LaunchSettingsPath)) {
                throw "Launch settings file not found: $LaunchSettingsPath"
            }
            $json = Get-Content -LiteralPath $LaunchSettingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
            $profileProperty = $json.profiles.PSObject.Properties[$LaunchProfile]
            if ($null -eq $profileProperty) {
                throw "Launch profile not found: $LaunchProfile"
            }
            return [string]$profileProperty.Value.connectionStrings.DefaultConnection
        }
        default {
            throw "Set WMS_DATA_QUALITY_SQL_CONNECTION_STRING or choose Appsettings/LaunchProfile. Never pass secrets in shell history."
        }
    }
}

function Get-Sha256Hex {
    param([Parameter(Mandatory)] [string]$Value)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Value)))).Replace("-", "")
    }
    finally {
        $sha.Dispose()
    }
}

function Get-SqlGuardText {
    param([Parameter(Mandatory)] [string]$SqlText)

    $builder = [Text.StringBuilder]::new($SqlText.Length)
    $state = "Code"
    $blockCommentDepth = 0

    for ($i = 0; $i -lt $SqlText.Length; $i++) {
        $current = $SqlText[$i]
        $next = if ($i + 1 -lt $SqlText.Length) { $SqlText[$i + 1] } else { [char]0 }
        $replacement = if ($current -eq "`r" -or $current -eq "`n") { $current } else { ' ' }

        switch ($state) {
            "Code" {
                if ($current -eq '-' -and $next -eq '-') {
                    [void]$builder.Append(' ')
                    [void]$builder.Append(' ')
                    $i++
                    $state = "LineComment"
                }
                elseif ($current -eq '/' -and $next -eq '*') {
                    [void]$builder.Append(' ')
                    [void]$builder.Append(' ')
                    $i++
                    $blockCommentDepth = 1
                    $state = "BlockComment"
                }
                elseif ($current -eq "'") {
                    [void]$builder.Append(' ')
                    $state = "StringLiteral"
                }
                elseif ($current -eq '[') {
                    [void]$builder.Append(' ')
                    $state = "BracketIdentifier"
                }
                elseif ($current -eq '"') {
                    [void]$builder.Append(' ')
                    $state = "QuotedIdentifier"
                }
                else {
                    [void]$builder.Append($current)
                }
            }
            "LineComment" {
                [void]$builder.Append($replacement)
                if ($current -eq "`r" -or $current -eq "`n") {
                    $state = "Code"
                }
            }
            "BlockComment" {
                if ($current -eq '/' -and $next -eq '*') {
                    [void]$builder.Append(' ')
                    [void]$builder.Append(' ')
                    $i++
                    $blockCommentDepth++
                }
                elseif ($current -eq '*' -and $next -eq '/') {
                    [void]$builder.Append(' ')
                    [void]$builder.Append(' ')
                    $i++
                    $blockCommentDepth--
                    if ($blockCommentDepth -eq 0) {
                        $state = "Code"
                    }
                }
                else {
                    [void]$builder.Append($replacement)
                }
            }
            "StringLiteral" {
                [void]$builder.Append($replacement)
                if ($current -eq "'" -and $next -eq "'") {
                    [void]$builder.Append(' ')
                    $i++
                }
                elseif ($current -eq "'") {
                    $state = "Code"
                }
            }
            "BracketIdentifier" {
                [void]$builder.Append($replacement)
                if ($current -eq ']' -and $next -eq ']') {
                    [void]$builder.Append(' ')
                    $i++
                }
                elseif ($current -eq ']') {
                    $state = "Code"
                }
            }
            "QuotedIdentifier" {
                [void]$builder.Append($replacement)
                if ($current -eq '"' -and $next -eq '"') {
                    [void]$builder.Append(' ')
                    $i++
                }
                elseif ($current -eq '"') {
                    $state = "Code"
                }
            }
        }
    }

    if ($state -notin @("Code", "LineComment")) {
        throw "Read-only guard rejected unterminated SQL $state."
    }

    return $builder.ToString()
}

if (-not (Test-Path -LiteralPath $SqlFile)) {
    throw "SQL audit file not found: $SqlFile"
}

$sql = Get-Content -LiteralPath $SqlFile -Raw -Encoding UTF8
$sqlWithoutComments = [regex]::Replace($sql, "/\*.*?\*/", "", [Text.RegularExpressions.RegexOptions]::Singleline)
$sqlWithoutComments = [regex]::Replace($sqlWithoutComments, "--[^\r\n]*", "")
$sqlForGuard = Get-SqlGuardText -SqlText $sql
$forbidden = [regex]::Match(
    $sqlForGuard,
    "\b(INSERT|UPDATE|DELETE|MERGE|TRUNCATE|DROP|ALTER|CREATE|EXEC|EXECUTE|GRANT|REVOKE|DENY|INTO|DBCC|BACKUP|RESTORE|BULK|KILL|SHUTDOWN|USE|OPENROWSET|OPENDATASOURCE|WAITFOR)\b|\bNEXT\s+VALUE\s+FOR\b",
    [Text.RegularExpressions.RegexOptions]::IgnoreCase)
if ($forbidden.Success) {
    throw "Read-only guard rejected SQL token: $($forbidden.Value.ToUpperInvariant())"
}

if ($ValidateSqlOnly) {
    Write-Output "SQL read-only guard validation passed: $SqlFile"
    return
}

$resolvedConnectionString = Resolve-ConnectionString
if ([string]::IsNullOrWhiteSpace($resolvedConnectionString)) {
    throw "The selected connection source does not contain a usable connection string."
}
$resolvedConnectionString = [regex]::Replace(
    $resolvedConnectionString,
    "(?i)(^|;)\s*ApplicationName\s*=",
    '$1Application Name=')

$builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder($resolvedConnectionString)
$targetFingerprint = Get-Sha256Hex ($builder.DataSource + "|" + $builder.InitialCatalog)
$builder["Application Name"] = "WMS-ReadOnly-Audit"
$builder["ApplicationIntent"] = "ReadOnly"
$safeConnectionString = $builder.ConnectionString

$outputDir = Split-Path -Parent $OutputPath
if ($outputDir -and -not (Test-Path -LiteralPath $outputDir)) {
    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
}

$issueCodes = @([regex]::Matches(
    $sqlWithoutComments,
    "SELECT\s+'(?<code>[^']+)'\s+AS\s+IssueCode",
    [Text.RegularExpressions.RegexOptions]::IgnoreCase) | ForEach-Object { $_.Groups["code"].Value })

$connection = New-Object System.Data.SqlClient.SqlConnection($safeConnectionString)
$connection.Open()
try {
    $command = $connection.CreateCommand()
    $command.CommandTimeout = 120
    $command.CommandText = $sql
    $reader = $command.ExecuteReader()
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("WMS_DATA_QUALITY_AUDIT")
    $lines.Add("GeneratedUtc`t$([DateTimeOffset]::UtcNow.ToString('o'))")
    $lines.Add("TargetFingerprint`t$targetFingerprint")
    $lines.Add("ConnectionSource`t$ConnectionSource")
    $lines.Add("Mode`t$(if ($DetailedOutput) { 'Detailed' } else { 'SummaryOnly' })")
    $lines.Add("")
    $resultIndex = 0
    do {
        $schema = $reader.GetSchemaTable()
        if ($null -eq $schema) { continue }
        $resultIndex++
        $headers = @()
        for ($i = 0; $i -lt $reader.FieldCount; $i++) { $headers += $reader.GetName($i) }
        $rowCount = 0
        $detailLines = New-Object System.Collections.Generic.List[string]
        if ($DetailedOutput) { $detailLines.Add(($headers -join "`t")) }
        $observedIssueCode = ""
        while ($reader.Read()) {
            $rowCount++
            if ($reader.FieldCount -gt 0 -and -not $reader.IsDBNull(0) -and [string]::IsNullOrWhiteSpace($observedIssueCode)) {
                $observedIssueCode = [string]$reader.GetValue(0)
            }
            if ($DetailedOutput) {
                $values = @()
                for ($i = 0; $i -lt $reader.FieldCount; $i++) {
                    $value = if ($reader.IsDBNull($i)) { "" } else { [string]$reader.GetValue($i) }
                    $values += ($value -replace "`r|`n|`t", " ")
                }
                $detailLines.Add(($values -join "`t"))
            }
        }

        $expectedIssueCode = if ($resultIndex -le $issueCodes.Count) { $issueCodes[$resultIndex - 1] } else { "RESULT_SET_$resultIndex" }
        $issueCode = if (-not [string]::IsNullOrWhiteSpace($observedIssueCode)) { $observedIssueCode } else { $expectedIssueCode }
        $lines.Add("## $issueCode")
        $lines.Add("Count`t$rowCount")
        if ($DetailedOutput) {
            foreach ($detailLine in $detailLines) { $lines.Add($detailLine) }
        }
        $lines.Add("")
    } while ($reader.NextResult())
    $reader.Close()
    [IO.File]::WriteAllLines($OutputPath, $lines, [Text.UTF8Encoding]::new($false))
    Write-Output "Audit target fingerprint: $targetFingerprint"
    Write-Output "Audit output: $OutputPath"
    Write-Output "Result sets: $resultIndex"
}
finally {
    $connection.Dispose()
    $resolvedConnectionString = $null
    $safeConnectionString = $null
}
