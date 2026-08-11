[CmdletBinding()]
param(
    [string]$RepositoryRoot = "",
    [string[]]$ProtectedConfigurationFiles = @("appsettings.json", "appsettings.Development.json", "Properties/launchSettings.json"),
    [switch]$IncludeGeneratedEvidence
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Split-Path -Parent $PSScriptRoot
}

function Add-ProtectedValues {
    param(
        [Parameter(Mandatory)] $Node,
        [string]$PropertyPath = "",
        [Parameter(Mandatory)] [AllowEmptyCollection()] [System.Collections.Generic.HashSet[string]]$Values
    )

    if ($null -eq $Node) { return }
    if ($Node -is [System.Management.Automation.PSCustomObject]) {
        foreach ($property in $Node.PSObject.Properties) {
            $path = if ($PropertyPath) { "$PropertyPath.$($property.Name)" } else { $property.Name }
            Add-ProtectedValues -Node $property.Value -PropertyPath $path -Values $Values
        }
        return
    }
    if ($Node -is [System.Collections.IEnumerable] -and $Node -isnot [string]) {
        foreach ($item in $Node) {
            Add-ProtectedValues -Node $item -PropertyPath $PropertyPath -Values $Values
        }
        return
    }

    $value = [string]$Node
    if ($value.Length -ge 8 -and $PropertyPath -match '(?i)(connectionstrings?|password|pwd|secret|api.?key|token|client.?secret)') {
        [void]$Values.Add($value)
    }
}

function Should-SkipPath {
    param([Parameter(Mandatory)] [string]$RelativePath)

    $normalized = $RelativePath.Replace([IO.Path]::DirectorySeparatorChar, '/')
    if ($ProtectedConfigurationFiles -contains $normalized) { return $true }
    if (($normalized -match '(^|/)(\.git|bin|obj|node_modules)(/|$)') -or
        ($normalized -match '^App_Data/(DataProtection-Keys|uploads)(/|$)')) {
        return $true
    }
    if ((-not $IncludeGeneratedEvidence) -and
        ($normalized -match '(^|/)(artifacts|logs|test-results|TestResults|playwright-report)(/|$)')) {
        return $true
    }
    return $false
}

$protectedValues = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($relativeConfig in $ProtectedConfigurationFiles) {
    $configPath = Join-Path $RepositoryRoot $relativeConfig
    if (-not (Test-Path -LiteralPath $configPath)) { continue }
    $configuration = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Add-ProtectedValues -Node $configuration -Values $protectedValues
}

$extensions = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
@('.cs', '.cshtml', '.js', '.ts', '.json', '.xml', '.config', '.props', '.targets', '.ps1', '.cmd', '.sql', '.md', '.txt', '.yml', '.yaml', '.toml', '.log', '.trx', '.html', '.csv') |
    ForEach-Object { [void]$extensions.Add($_) }

$matchedPaths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$repositoryPrefix = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
foreach ($file in Get-ChildItem -LiteralPath $RepositoryRoot -Recurse -File -Force) {
    $relative = $file.FullName.Substring($repositoryPrefix.Length)
    if (Should-SkipPath -RelativePath $relative) { continue }
    if (-not $extensions.Contains($file.Extension)) { continue }

    $content = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
    if ($null -eq $content) { continue }
    foreach ($protectedValue in $protectedValues) {
        if ($content.IndexOf($protectedValue, [StringComparison]::Ordinal) -ge 0) {
            [void]$matchedPaths.Add($relative)
            break
        }
    }
}

Write-Output "WMS_PROTECTED_SECRET_SCAN"
Write-Output "ProtectedValueCount=$($protectedValues.Count)"
Write-Output "GeneratedEvidenceIncluded=$($IncludeGeneratedEvidence.IsPresent)"
Write-Output "MatchCount=$($matchedPaths.Count)"
foreach ($path in $matchedPaths | Sort-Object) {
    Write-Output "MatchedPath=$path"
}

if ($matchedPaths.Count -gt 0) {
    throw "Protected secret values were copied outside protected configuration files. Values are intentionally not displayed."
}
