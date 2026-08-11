param(
    [string]$BaseUrl = $env:WMS_BASE_URL,
    [string]$DataQualityAuditUrl = $env:WMS_DATA_QUALITY_AUDIT_URL,
    [string]$DataQualityAuthHeader = $env:WMS_DATA_QUALITY_AUTH_HEADER,
    [switch]$SkipVisual,
    [switch]$SkipPackage,
    [switch]$SkipDr,
    [switch]$SkipTextAudit,
    [switch]$SkipDataQualityAudit,
    [switch]$IncludeK6,
    [switch]$RequireK6,
    [switch]$RequireExternalEvidence
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$artifactDir = Join-Path $root "artifacts\tier1-evidence"
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
$logPath = Join-Path $artifactDir "tier1-evidence-gate.log"
$manifestPath = Join-Path $artifactDir "tier1-evidence-manifest.json"
$results = New-Object System.Collections.Generic.List[object]

function Write-GateLog([string]$message) {
    $line = "[{0:yyyy-MM-dd HH:mm:ss}] {1}" -f (Get-Date), $message
    $line | Tee-Object -FilePath $logPath -Append
}

function Add-GateResult([string]$name, [string]$status, [string]$detail, [string]$artifact = "") {
    $results.Add([ordered]@{
        name = $name
        status = $status
        detail = $detail
        artifact = $artifact
        checkedAt = (Get-Date).ToString("O")
    }) | Out-Null
    Write-GateLog "$status $name :: $detail"
}

function New-TextFromCodePoints([int[]]$codePoints) {
    $chars = foreach ($codePoint in $codePoints) { [char]$codePoint }
    return -join $chars
}

function Invoke-CommandGate([string]$name, [scriptblock]$script, [string]$artifact) {
    Write-GateLog "START $name"
    try {
        $global:LASTEXITCODE = 0
        $previousErrorActionPreference = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        try {
            $output = & $script 2>&1
        } finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }
        $output | Set-Content -Path $artifact -Encoding UTF8
        if ($global:LASTEXITCODE -ne 0) {
            throw "$name exited with code $global:LASTEXITCODE"
        }
        Add-GateResult $name "Pass" "Command completed." $artifact
    } catch {
        Add-GateResult $name "Failed" $_.Exception.Message $artifact
        throw
    }
}

function Invoke-TextAudit {
    $scanRoots = @("Controllers", "Views", "Models", "Services", "Common", "wwwroot", "docs", "scripts")
    $extensions = @(".cs", ".cshtml", ".js", ".css", ".html", ".md", ".ps1")
    $ignoredPathParts = @("\wwwroot\lib\", "\wwwroot\vendor\", "\bin\", "\obj\", "\node_modules\", "\artifacts\", "\test-results\", "\TestResults\")
    $knownBrokenFragments = @(
        (New-TextFromCodePoints @(0x00C4, 0x2018)),
        (New-TextFromCodePoints @(0x00C4, 0x0090)),
        (New-TextFromCodePoints @(0x00C6, 0x00B0)),
        (New-TextFromCodePoints @(0x00C6, 0x00A1)),
        (New-TextFromCodePoints @(0x00E1, 0x00BA)),
        (New-TextFromCodePoints @(0x00E1, 0x00BB)),
        (New-TextFromCodePoints @(0x00C3, 0x00A3)),
        (New-TextFromCodePoints @(0x00C3, 0x00AA)),
        (New-TextFromCodePoints @(0x00C3, 0x00B3)),
        (New-TextFromCodePoints @(0x00C3, 0x00A1)),
        (New-TextFromCodePoints @(0x00C3, 0x00A0)),
        (New-TextFromCodePoints @(0x00C3, 0x00B4)),
        (New-TextFromCodePoints @(0x00C3, 0x00A2)),
        (New-TextFromCodePoints @(0x00C3, 0x00B9)),
        (New-TextFromCodePoints @(0x00C3, 0x00BA)),
        (New-TextFromCodePoints @(0x00E2, 0x20AC, 0x201D)),
        (New-TextFromCodePoints @(0x00E2, 0x20AC, 0x201C)),
        (New-TextFromCodePoints @(0x00E2, 0x20AC, 0x00A6)),
        (New-TextFromCodePoints @(0x00E2, 0x20AC)),
        (New-TextFromCodePoints @(0x00EF, 0x00BF, 0x00BD)),
        ([string][char]0xFFFD)
    )
    $failures = New-Object System.Collections.Generic.List[string]

    foreach ($relativeRoot in $scanRoots) {
        $path = Join-Path $root $relativeRoot
        if (-not (Test-Path $path)) { continue }

        Get-ChildItem -LiteralPath $path -Recurse -File | Where-Object {
            $candidatePath = $_.FullName
            $extensions -contains $_.Extension -and -not ($ignoredPathParts | Where-Object { $candidatePath -like "*$_*" })
        } | ForEach-Object {
            $fullName = $_.FullName
            $relative = $fullName.Substring($root.Path.Length).TrimStart('\', '/')
            $content = Get-Content -LiteralPath $fullName -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
            foreach ($fragment in $knownBrokenFragments) {
                if ($fragment -and $content.IndexOf($fragment, [System.StringComparison]::Ordinal) -ge 0) {
                    $failures.Add("$relative contains a known mojibake marker.")
                    break
                }
            }
        }
    }

    if ($failures.Count -gt 0) {
        $artifact = Join-Path $artifactDir "text-audit-failures.txt"
        $failures | Set-Content -Path $artifact -Encoding UTF8
        Add-GateResult "text-ui-microcopy-audit" "Failed" "$($failures.Count) mojibake marker(s) found." $artifact
        throw "Text/UI microcopy audit failed."
    }

    Add-GateResult "text-ui-microcopy-audit" "Pass" "No known mojibake markers found in scanned source/docs/scripts."
}

function Invoke-DataQualityAudit {
    if (-not $DataQualityAuditUrl) {
        Add-GateResult "data-quality-audit" "Blocked" "WMS_DATA_QUALITY_AUDIT_URL was not provided. Use an authenticated staging/admin URL for /System/DataQualityAudit."
        return
    }

    $headers = @{}
    $webSession = $null
    if ($DataQualityAuthHeader) {
        $splitIndex = $DataQualityAuthHeader.IndexOf(":")
        if ($splitIndex -gt 0) {
            $headerName = $DataQualityAuthHeader.Substring(0, $splitIndex).Trim()
            $headerValue = $DataQualityAuthHeader.Substring($splitIndex + 1).Trim()
            if ($headerName -ieq "Cookie") {
                $webSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
                $targetUri = [Uri]$DataQualityAuditUrl
                foreach ($cookiePair in ($headerValue -split ";")) {
                    $pair = $cookiePair.Trim()
                    if (-not $pair) { continue }
                    $equalsIndex = $pair.IndexOf("=")
                    if ($equalsIndex -le 0) { continue }
                    $cookieName = $pair.Substring(0, $equalsIndex).Trim()
                    $cookieValue = $pair.Substring($equalsIndex + 1).Trim()
                    $cookie = New-Object System.Net.Cookie($cookieName, $cookieValue, "/", $targetUri.Host)
                    $webSession.Cookies.Add($targetUri, $cookie)
                }
            } else {
                $headers[$headerName] = $headerValue
            }
        }
    }

    $artifact = Join-Path $artifactDir "data-quality-audit.json"
    try {
        if ($webSession) {
            $response = Invoke-WebRequest -Uri $DataQualityAuditUrl -Headers $headers -WebSession $webSession -UseBasicParsing -TimeoutSec 60
        } else {
            $response = Invoke-WebRequest -Uri $DataQualityAuditUrl -Headers $headers -UseBasicParsing -TimeoutSec 60
        }
        $response.Content | Set-Content -Path $artifact -Encoding UTF8
        $json = $response.Content | ConvertFrom-Json
        if ($json.status -ne "Passed") {
            Add-GateResult "data-quality-audit" "Failed" "Data quality status=$($json.status), critical=$($json.criticalCount), error=$($json.errorCount), warning=$($json.warningCount)." $artifact
            throw "Data quality audit failed."
        }
        Add-GateResult "data-quality-audit" "Pass" "Data quality status passed." $artifact
    } catch {
        Add-GateResult "data-quality-audit" "Failed" $_.Exception.Message $artifact
        throw
    }
}

function Invoke-ExternalEvidenceCheck {
    $requiredIds = @(
        "HW-RF-001", "HW-RF-002", "HW-RF-003",
        "HW-SCAN-001", "HW-SCAN-002", "HW-SCAN-003",
        "HW-PRINT-001", "HW-PRINT-002",
        "LOAD-001", "LOAD-002", "LOAD-003", "LOAD-004",
        "DR-001", "DR-002", "DR-003", "DR-004", "DR-005",
        "INT-ERP-001", "INT-TMS-001", "INT-OMS-001", "INT-MHE-001", "INT-CAR-001",
        "OBS-001", "OBS-002", "OBS-003", "OBS-004", "OBS-005", "OBS-006"
    )
    $evidenceRoot = Join-Path $root "artifacts\production-evidence"
    $missing = New-Object System.Collections.Generic.List[string]

    foreach ($id in $requiredIds) {
        $found = $false
        if (Test-Path $evidenceRoot) {
            $found = [bool](Get-ChildItem -LiteralPath $evidenceRoot -Recurse -File -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -like "$id*" } |
                Select-Object -First 1)
        }
        if (-not $found) {
            $missing.Add($id)
        }
    }

    if ($missing.Count -gt 0) {
        $artifact = Join-Path $artifactDir "external-evidence-blocked.txt"
        $missing | Set-Content -Path $artifact -Encoding UTF8
        Add-GateResult "external-production-evidence" "Blocked" "$($missing.Count) external evidence artifact(s) missing. Production Tier-1 cannot be marked 100%." $artifact
        return
    }

    Add-GateResult "external-production-evidence" "Pass" "All required external evidence IDs have matching artifacts."
}

Push-Location $root
try {
    Write-GateLog "Tier-1 evidence gate started. Secrets and connection strings are not printed."

    Invoke-CommandGate "dotnet-build" { dotnet build WMS.sln --no-restore -v:minimal } (Join-Path $artifactDir "dotnet-build.log")
    Invoke-CommandGate "dotnet-test" { dotnet test WMS.Tests\WMS.Tests.csproj --no-restore --logger "console;verbosity=minimal" } (Join-Path $artifactDir "dotnet-test.log")
    Invoke-CommandGate "nuget-vulnerability-scan" { dotnet list WMS.sln package --vulnerable --include-transitive --no-restore } (Join-Path $artifactDir "nuget-vulnerable.log")
    Invoke-CommandGate "npm-vulnerability-scan" { npm audit --json } (Join-Path $artifactDir "npm-audit.json")
    Invoke-CommandGate "protected-secret-exact-value-scan" { & .\scripts\Invoke-WmsProtectedSecretScan.ps1 } (Join-Path $artifactDir "protected-secret-scan.log")

    if ($SkipTextAudit) {
        Add-GateResult "text-ui-microcopy-audit" "Skipped" "-SkipTextAudit was provided."
    } else {
        Invoke-TextAudit
    }

    if ($SkipVisual) {
        Add-GateResult "visual-verification" "Skipped" "-SkipVisual was provided."
    } else {
        if ($RequireK6 -or $IncludeK6) {
            Invoke-CommandGate "visual-verification" { & .\scripts\Run-WmsVerification.ps1 -SkipBuild -SkipTests -IncludeK6 } (Join-Path $artifactDir "visual-verification.log")
        } else {
            Invoke-CommandGate "visual-verification" { & .\scripts\Run-WmsVerification.ps1 -SkipBuild -SkipTests -SkipK6 } (Join-Path $artifactDir "visual-verification.log")
        }
    }

    if ($SkipPackage) {
        Add-GateResult "production-package-hygiene" "Skipped" "-SkipPackage was provided."
    } else {
        Invoke-CommandGate "production-package-hygiene" { & .\scripts\Build-ProductionPackage.ps1 -NoRestore } (Join-Path $artifactDir "production-package.log")
    }

    if ($SkipDataQualityAudit) {
        Add-GateResult "data-quality-audit" "Skipped" "-SkipDataQualityAudit was provided."
    } else {
        Invoke-DataQualityAudit
    }

    if ($SkipDr) {
        Add-GateResult "dr-ha-evidence" "Skipped" "-SkipDr was provided."
    } else {
        $drArtifact = Join-Path $artifactDir "dr-evidence-wrapper.log"
        $before = @(Get-ChildItem -Path (Join-Path $root "artifacts\dr") -Filter "dr-evidence-*.log" -ErrorAction SilentlyContinue)
        Invoke-CommandGate "dr-ha-evidence-script" { & .\scripts\Invoke-WmsDrEvidence.ps1 } $drArtifact
        $after = Get-ChildItem -Path (Join-Path $root "artifacts\dr") -Filter "dr-evidence-*.log" -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1
        if ($after -and -not (@($before.FullName) -contains $after.FullName)) {
            $content = Get-Content -LiteralPath $after.FullName -Raw
            if ($content -match "BLOCKED|BLOCKED_OR_FAILED") {
                Add-GateResult "dr-ha-evidence" "Blocked" "DR script ran but restore/SQL/health evidence is incomplete." $after.FullName
            } else {
                Add-GateResult "dr-ha-evidence" "Pass" "DR script produced no blocked markers." $after.FullName
            }
        }
    }

    Invoke-ExternalEvidenceCheck

    $failed = @($results | Where-Object { $_.status -eq "Failed" })
    $blocked = @($results | Where-Object { $_.status -eq "Blocked" })
    $overall = if ($failed.Count -gt 0) {
        "Failed"
    } elseif ($blocked.Count -gt 0) {
        "Blocked"
    } else {
        "Passed"
    }

    [ordered]@{
        generatedAt = (Get-Date).ToString("O")
        overallStatus = $overall
        productionTier1CanBeMarked100 = ($overall -eq "Passed")
        rule = "Production Tier-1 can be marked 100% only when every local gate and every external evidence ID has passed."
        results = $results
    } | ConvertTo-Json -Depth 8 | Set-Content -Path $manifestPath -Encoding UTF8

    Write-GateLog "Tier-1 evidence manifest: $manifestPath"

    if ($failed.Count -gt 0) {
        throw "Tier-1 evidence gate failed. See $manifestPath"
    }

    if ($RequireExternalEvidence -and $blocked.Count -gt 0) {
        throw "Tier-1 production evidence is blocked by missing external artifacts. See $manifestPath"
    }

    Write-GateLog "Tier-1 evidence gate completed with overallStatus=$overall."
} finally {
    Pop-Location
}
