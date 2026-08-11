[CmdletBinding()]
param(
    [string]$ConnectionString = $env:WMS_ROLE_AUDIT_CONNECTION_STRING,
    [int]$Port = 5088,
    [string]$AdminUserName = "AUDIT_TEST_PW_ADMIN_20260711",
    [string]$AdminAuthState = "tests/visual/.auth/audit-local-admin.json",
    [string]$OutputRoot = "artifacts/role-e2e"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$exePath = Join-Path $repoRoot "bin/Debug/net8.0/WMS.exe"
$baseUrl = "http://127.0.0.1:$Port"
$roleRunId = [DateTimeOffset]::UtcNow.ToString("yyyyMMddHHmmssfff")
$roleUsers = [ordered]@{
    manager = "AUDIT_TEST_MGR_$roleRunId"
    inbound = "AUDIT_TEST_IN_$roleRunId"
    outbound = "AUDIT_TEST_OUT_$roleRunId"
    inventory = "AUDIT_TEST_INV_$roleRunId"
    transport = "AUDIT_TEST_TRN_$roleRunId"
    report = "AUDIT_TEST_RPT_$roleRunId"
    viewer = "AUDIT_TEST_VIEW_$roleRunId"
}

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw "WMS_ROLE_AUDIT_CONNECTION_STRING is required."
}
if (-not $AdminUserName.StartsWith("AUDIT_TEST_", [StringComparison]::Ordinal)) {
    throw "AdminUserName must use the AUDIT_TEST_ prefix."
}

$connectionBuilder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($ConnectionString)
$dataSource = $connectionBuilder.DataSource.Trim().ToLowerInvariant()
$databaseName = $connectionBuilder.InitialCatalog.Trim()
$isLocalSource = $dataSource -match '^(\.\\|\(localdb\)|localhost|127\.|::1|\[::1\])'
if (-not $isLocalSource -or -not $databaseName.StartsWith("AUDIT_TEST_", [StringComparison]::Ordinal)) {
    throw "Role evidence refuses non-local or non-AUDIT_TEST_ databases."
}

$outputPath = Join-Path $repoRoot $OutputRoot
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
$passwordBytes = [byte[]]::new(24)
$randomGenerator = [Security.Cryptography.RandomNumberGenerator]::Create()
try {
    $randomGenerator.GetBytes($passwordBytes)
}
finally {
    $randomGenerator.Dispose()
}
$testPassword = "Aa9!" + ([BitConverter]::ToString($passwordBytes)).Replace("-", "")
$resetTokenBytes = [byte[]]::new(32)
$resetTokenGenerator = [Security.Cryptography.RandomNumberGenerator]::Create()
try {
    $resetTokenGenerator.GetBytes($resetTokenBytes)
}
finally {
    $resetTokenGenerator.Dispose()
}
$devResetToken = ([BitConverter]::ToString($resetTokenBytes)).Replace("-", "")
$activeProcess = $null

function Stop-VerifiedWmsProcess {
    $listener = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $listener) { return }

    $process = Get-Process -Id $listener.OwningProcess -ErrorAction Stop
    $expected = (Resolve-Path -LiteralPath $exePath).Path
    if ($process.Path -ne $expected) {
        throw "Refusing to stop an unexpected process on the role-audit port."
    }

    Stop-Process -Id $process.Id -Force
    $process.WaitForExit()
}

function Start-IsolatedWmsProcess([string]$BypassUser, [string]$LogName) {
    if (-not $BypassUser.StartsWith("AUDIT_TEST_", [StringComparison]::Ordinal)) {
        throw "Local verification user must use the AUDIT_TEST_ prefix."
    }

    $env:ASPNETCORE_ENVIRONMENT = "Development"
    $env:ASPNETCORE_URLS = $baseUrl
    $env:ConnectionStrings__DefaultConnection = $ConnectionString
    $env:BackgroundWorkers__Enabled = "false"
    $env:StartupInitialization__RbacSeedEnabled = "false"
    $env:LocalVerification__Enabled = "true"
    $env:LocalVerification__BypassMfaForLoopback = "true"
    $env:LocalVerification__UserName = $BypassUser
    $env:DevResetToken = $devResetToken

    $stdout = Join-Path $outputPath "$LogName.out.log"
    $stderr = Join-Path $outputPath "$LogName.err.log"
    $script:activeProcess = Start-Process -FilePath $exePath -WorkingDirectory $repoRoot -WindowStyle Hidden `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru

    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($script:activeProcess.HasExited) { throw "Local WMS role-audit process exited before readiness." }
        try {
            $response = Invoke-WebRequest -Uri "$baseUrl/health/live" -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -lt 500) { return }
        }
        catch { Start-Sleep -Milliseconds 500 }
    }
    throw "Local WMS role-audit process did not become ready."
}

function Invoke-Playwright([string[]]$Arguments) {
    & npx playwright @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Playwright exited with code $LASTEXITCODE." }
}

Push-Location $repoRoot
try {
    Stop-VerifiedWmsProcess
    dotnet build WMS.csproj --no-restore
    if ($LASTEXITCODE -ne 0) { throw "WMS build failed with code $LASTEXITCODE." }

    Start-IsolatedWmsProcess -BypassUser $AdminUserName -LogName "role-fixture-admin"
    $env:WMS_BASE_URL = $baseUrl
    $env:WMS_TEST_USER = $AdminUserName
    $env:WMS_TEST_PASSWORD = $testPassword
    $env:WMS_AUTH_STATE = $AdminAuthState
    $env:WMS_TEST_RESET_TOKEN = $devResetToken
    Invoke-Playwright @("test", "-c", "tests/visual/playwright.auth.config.ts")
    $env:WMS_TEST_RESET_TOKEN = $null
    $env:WMS_TEST_USER = $null
    $env:WMS_TEST_PASSWORD = $null
    $env:WMS_AUTH_STATE = $null
    $env:WMS_ADMIN_AUTH_STATE = $AdminAuthState
    $env:WMS_ROLE_TEST_PASSWORD = $testPassword
    $env:WMS_ROLE_TEST_RUN_ID = $roleRunId
    Invoke-Playwright @("test", "-c", "tests/visual/playwright.role-fixture.config.ts")
    Stop-VerifiedWmsProcess

    foreach ($entry in $roleUsers.GetEnumerator()) {
        Start-IsolatedWmsProcess -BypassUser $entry.Value -LogName "role-auth-$($entry.Key)"
        $env:WMS_TEST_USER = $entry.Value
        $env:WMS_TEST_PASSWORD = $testPassword
        $env:WMS_AUTH_STATE = "tests/visual/.auth/audit-role-$($entry.Key).json"
        Invoke-Playwright @("test", "-c", "tests/visual/playwright.auth.config.ts")
        Stop-VerifiedWmsProcess
    }

    $env:WMS_TEST_USER = $null
    $env:WMS_TEST_PASSWORD = $null
    $env:WMS_AUTH_STATE = $null
    Start-IsolatedWmsProcess -BypassUser $AdminUserName -LogName "role-access-matrix"
    Invoke-Playwright @("test", "-c", "tests/visual/playwright.role-access.config.ts")

    @(
        "WMS_LOCAL_ROLE_ACCESS_EVIDENCE",
        "GeneratedUtc`t$([DateTimeOffset]::UtcNow.ToString('o'))",
        "Database`t$databaseName",
        "RolesTested`t$($roleUsers.Count)",
        "FixturePrefix`tAUDIT_TEST_",
        "FixtureCorrelationId`t$roleRunId",
        "Result`tPASS",
        "SecretValuesLogged`tNO"
    ) | Set-Content -LiteralPath (Join-Path $outputPath "role-access-summary.txt") -Encoding UTF8
}
finally {
    try { Stop-VerifiedWmsProcess } catch { }
    $testPassword = $null
    $devResetToken = $null
    $env:WMS_ROLE_TEST_PASSWORD = $null
    $env:WMS_ROLE_TEST_RUN_ID = $null
    $env:WMS_TEST_PASSWORD = $null
    $env:WMS_TEST_USER = $null
    $env:WMS_AUTH_STATE = $null
    $env:WMS_ADMIN_AUTH_STATE = $null
    $env:WMS_TEST_RESET_TOKEN = $null
    $env:DevResetToken = $null
    $env:ConnectionStrings__DefaultConnection = $null
    Pop-Location
}
