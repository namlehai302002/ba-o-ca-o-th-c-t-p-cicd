[CmdletBinding()]
param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot),
    [string]$OutputDirectory = "artifacts/full-audit"
)

$ErrorActionPreference = "Stop"
$rootPath = (Resolve-Path -LiteralPath $Root).Path
$outputPath = Join-Path $rootPath $OutputDirectory
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

function Get-RelativePath {
    param([Parameter(Mandatory)] [string]$Path)
    $prefix = $rootPath.TrimEnd("\", "/") + [IO.Path]::DirectorySeparatorChar
    if (-not $Path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside repository root: $Path"
    }

    return $Path.Substring($prefix.Length).Replace("\", "/")
}

function Write-CsvUtf8 {
    param(
        [Parameter(Mandatory)] [object[]]$Rows,
        [Parameter(Mandatory)] [string]$Name
    )

    $path = Join-Path $outputPath $Name
    $Rows | Export-Csv -LiteralPath $path -NoTypeInformation -Encoding utf8
    return $path
}

$actionRows = @()
$controllerFiles = Get-ChildItem -LiteralPath (Join-Path $rootPath "Controllers") -File -Filter "*.cs" | Sort-Object Name
$controllerMetadata = @{}
foreach ($file in $controllerFiles) {
    $lines = [IO.File]::ReadAllLines($file.FullName)
    $classMatch = $lines | Select-String -Pattern "\bclass\s+([A-Za-z0-9_]+Controller)\b" | Select-Object -First 1
    if ($null -eq $classMatch) { continue }

    $className = $classMatch.Matches[0].Groups[1].Value
    $classLine = $classMatch.LineNumber - 1
    $classAttributes = New-Object System.Collections.Generic.List[string]
    for ($index = $classLine - 1; $index -ge 0; $index--) {
        $trimmed = $lines[$index].Trim()
        if ($trimmed.StartsWith("[")) {
            $classAttributes.Insert(0, $trimmed)
            continue
        }
        if ([string]::IsNullOrWhiteSpace($trimmed)) { continue }
        break
    }

    $classAttributeText = [string]::Join(" ", $classAttributes)
    $routeMatches = [regex]::Matches($classAttributeText, '\[Route\("(?<template>[^"]+)"\)\]')
    $routes = @($routeMatches | ForEach-Object { $_.Groups["template"].Value })
    $authorizes = @([regex]::Matches($classAttributeText, '\[Authorize(?:\((?<value>[^\]]+)\))?\]') | ForEach-Object {
        if ($_.Groups["value"].Success) { $_.Groups["value"].Value } else { "Authenticated" }
    })

    if (-not $controllerMetadata.ContainsKey($className)) {
        $controllerMetadata[$className] = [pscustomobject]@{
            Routes = New-Object System.Collections.Generic.List[string]
            Authorizes = New-Object System.Collections.Generic.List[string]
            AllowsAnonymous = $false
            UsesApiKeyBoundary = $false
        }
    }

    $metadata = $controllerMetadata[$className]
    foreach ($route in $routes) {
        if (-not $metadata.Routes.Contains($route)) { $metadata.Routes.Add($route) }
    }
    foreach ($authorize in $authorizes) {
        if (-not $metadata.Authorizes.Contains($authorize)) { $metadata.Authorizes.Add($authorize) }
    }
    if ($classAttributeText -match '\[AllowAnonymous\]') { $metadata.AllowsAnonymous = $true }
    if ($classAttributeText -match '\[ApiKeyAllowAnonymous\]') { $metadata.UsesApiKeyBoundary = $true }
}

foreach ($file in $controllerFiles) {
    $lines = [IO.File]::ReadAllLines($file.FullName)
    $classMatch = $lines | Select-String -Pattern "\bclass\s+([A-Za-z0-9_]+Controller)\b" | Select-Object -First 1
    if ($null -eq $classMatch) {
        continue
    }

    $className = $classMatch.Matches[0].Groups[1].Value
    $controller = $className.Substring(0, $className.Length - "Controller".Length)
    $metadata = $controllerMetadata[$className]
    $attributes = New-Object System.Collections.Generic.List[string]
    for ($index = 0; $index -lt $lines.Length; $index++) {
        $trimmed = $lines[$index].Trim()
        if ($trimmed.StartsWith("[")) {
            $attributes.Add($trimmed)
            continue
        }

        $methodMatch = [regex]::Match(
            $lines[$index],
            "^\s*public\s+(?:async\s+)?(?<return>[A-Za-z0-9_<>,?\[\]\.]+)\s+(?<name>[A-Za-z0-9_]+)\s*\(")
        if (-not $methodMatch.Success) {
            if ($trimmed.Length -gt 0 -and -not $trimmed.StartsWith("//") -and -not $trimmed.StartsWith("///")) {
                $attributes.Clear()
            }
            continue
        }

        $methodName = $methodMatch.Groups["name"].Value
        $returnType = $methodMatch.Groups["return"].Value
        $attributeText = [string]::Join(" ", $attributes)
        $httpMatches = [regex]::Matches($attributeText, '\[(HttpGet|HttpPost|HttpPut|HttpPatch|HttpDelete)(?:\("(?<template>[^"]*)"\))?')
        $isActionReturn = $returnType -match "IActionResult|ActionResult|JsonResult|FileResult|ContentResult|Redirect"
        if ($httpMatches.Count -eq 0 -and -not $isActionReturn) {
            $attributes.Clear()
            continue
        }

        $verbs = if ($httpMatches.Count -gt 0) {
            ($httpMatches | ForEach-Object { $_.Groups[1].Value.Substring(4).ToUpperInvariant() } | Sort-Object -Unique) -join ","
        } else {
            "CONVENTIONAL"
        }
        $templates = @($httpMatches | ForEach-Object { $_.Groups["template"].Value } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        $actionRouteMatches = [regex]::Matches($attributeText, '\[Route\("(?<template>[^"]+)"\)\]')
        $templates += @($actionRouteMatches | ForEach-Object { $_.Groups["template"].Value })
        $classRoutes = @($metadata.Routes | ForEach-Object { $_.Replace("[controller]", $controller) })
        $resolvedRoutes = New-Object System.Collections.Generic.List[string]
        if ($templates.Count -gt 0) {
            foreach ($template in $templates) {
                if ($template.StartsWith("/") -or $template.StartsWith("~/")) {
                    $resolvedRoutes.Add($template.TrimStart("~"))
                } elseif ($classRoutes.Count -gt 0) {
                    foreach ($classRoute in $classRoutes) {
                        $resolvedRoutes.Add("/" + $classRoute.Trim("/") + "/" + $template.Trim("/"))
                    }
                } else {
                    $resolvedRoutes.Add("/" + $template.Trim("/"))
                }
            }
        } elseif ($classRoutes.Count -gt 0 -and $httpMatches.Count -gt 0) {
            foreach ($classRoute in $classRoutes) { $resolvedRoutes.Add("/" + $classRoute.Trim("/")) }
        } else {
            $resolvedRoutes.Add("/$controller/$methodName")
        }
        $route = @($resolvedRoutes | Sort-Object -Unique) -join " | "
        $methodAuthorizes = @([regex]::Matches($attributeText, '\[Authorize(?:\((?<value>[^\]]+)\))?\]') | ForEach-Object {
            if ($_.Groups["value"].Success) { $_.Groups["value"].Value } else { "Authenticated" }
        })
        $authorize = @($metadata.Authorizes + $methodAuthorizes | Sort-Object -Unique) -join " | "
        $allowAnonymous = $metadata.AllowsAnonymous -or $attributeText -match "\[AllowAnonymous\]"
        $securityBoundary = if ($metadata.UsesApiKeyBoundary) {
            "ApiKeyValidation"
        } elseif ($allowAnonymous) {
            "Anonymous"
        } elseif (-not [string]::IsNullOrWhiteSpace($authorize)) {
            "AuthorizeAttribute"
        } else {
            "GlobalAuthenticatedFilter"
        }

        $actionRows += [pscustomobject]@{
            Controller = $controller
            Action = $methodName
            Verb = $verbs
            Route = $route
            File = Get-RelativePath $file.FullName
            Line = $index + 1
            ReturnType = $returnType
            AuthorizeAttribute = $authorize
            AllowAnonymous = $allowAnonymous
            SecurityBoundary = $securityBoundary
            RuntimeVerification = "UNKNOWN"
            Evidence = ""
        }
        $attributes.Clear()
    }
}

$navigationRows = @()
$viewFiles = Get-ChildItem -LiteralPath (Join-Path $rootPath "Views") -Recurse -File -Filter "*.cshtml" | Sort-Object FullName
foreach ($file in $viewFiles) {
    $lines = [IO.File]::ReadAllLines($file.FullName)
    for ($index = 0; $index -lt $lines.Length; $index++) {
        $line = $lines[$index]
        $controllerMatch = [regex]::Match($line, 'asp-controller="(?<value>[^"]+)"')
        $actionMatch = [regex]::Match($line, 'asp-action="(?<value>[^"]+)"')
        $hrefMatches = [regex]::Matches($line, 'href="(?<value>/[^"#?]+)')

        if ($controllerMatch.Success -or $actionMatch.Success) {
            $navigationRows += [pscustomobject]@{
                SourceFile = Get-RelativePath $file.FullName
                Line = $index + 1
                LinkType = "tag-helper"
                Controller = $controllerMatch.Groups["value"].Value
                Action = $actionMatch.Groups["value"].Value
                Route = ""
                ConsumerVerification = "UNKNOWN"
            }
        }

        foreach ($hrefMatch in $hrefMatches) {
            $navigationRows += [pscustomobject]@{
                SourceFile = Get-RelativePath $file.FullName
                Line = $index + 1
                LinkType = "literal-href"
                Controller = ""
                Action = ""
                Route = $hrefMatch.Groups["value"].Value
                ConsumerVerification = "UNKNOWN"
            }
        }
    }
}

$programPath = Join-Path $rootPath "Program.cs"
$programLines = [IO.File]::ReadAllLines($programPath)
$serviceRows = @()
for ($index = 0; $index -lt $programLines.Length; $index++) {
    $match = [regex]::Match($programLines[$index], "Add(?<lifetime>Singleton|Scoped|Transient|HostedService)<(?<types>[^>]+)>")
    if (-not $match.Success) { continue }
    $types = $match.Groups["types"].Value.Split(",") | ForEach-Object { $_.Trim() }
    $serviceRows += [pscustomobject]@{
        Lifetime = $match.Groups["lifetime"].Value
        Service = $types[0]
        Implementation = if ($types.Count -gt 1) { $types[1] } else { $types[0] }
        File = "Program.cs"
        Line = $index + 1
    }
}

$dbContextPath = Join-Path $rootPath "Data\AppDbContext.cs"
$dbLines = [IO.File]::ReadAllLines($dbContextPath)
$dbSetRows = @()
for ($index = 0; $index -lt $dbLines.Length; $index++) {
    $match = [regex]::Match($dbLines[$index], "DbSet<(?<entity>[^>]+)>\s+(?<property>[A-Za-z0-9_]+)")
    if (-not $match.Success) { continue }
    $dbSetRows += [pscustomobject]@{
        Entity = $match.Groups["entity"].Value
        DbSet = $match.Groups["property"].Value
        File = "Data/AppDbContext.cs"
        Line = $index + 1
        RuntimeVerification = "UNKNOWN"
    }
}

$writeRows = @()
$writeFiles = Get-ChildItem -LiteralPath (Join-Path $rootPath "Controllers"), (Join-Path $rootPath "Services") -Recurse -File -Filter "*.cs" | Sort-Object FullName
$writePattern = "\.(?<property>Quantity|ReservedQty)\s*(?<operator>\+\+|--|[+\-*/]?=)|new\s+ItemLocation\b"
foreach ($file in $writeFiles) {
    $lines = [IO.File]::ReadAllLines($file.FullName)
    for ($index = 0; $index -lt $lines.Length; $index++) {
        $match = [regex]::Match($lines[$index], $writePattern)
        if (-not $match.Success) { continue }
        $writeRows += [pscustomobject]@{
            File = Get-RelativePath $file.FullName
            Line = $index + 1
            Property = if ($match.Groups["property"].Success) { $match.Groups["property"].Value } else { "ItemLocation" }
            Operator = if ($match.Groups["operator"].Success) { $match.Groups["operator"].Value } else { "new" }
            RuntimePath = "UNKNOWN"
            TransactionBoundary = "UNKNOWN"
            LedgerContext = "UNKNOWN"
            FindingIds = ""
        }
    }
}

$stateRows = @()
$statePattern = "\.(?<property>InboundStatus|FulfillmentStatus|IsPosted|IsCancelled|Status)\s*(?<operator>\?\?=|=)\s*(?<value>[^;]+)"
foreach ($file in $writeFiles) {
    $lines = [IO.File]::ReadAllLines($file.FullName)
    for ($index = 0; $index -lt $lines.Length; $index++) {
        $match = [regex]::Match($lines[$index], $statePattern)
        if (-not $match.Success) { continue }
        $stateRows += [pscustomobject]@{
            File = Get-RelativePath $file.FullName
            Line = $index + 1
            Property = $match.Groups["property"].Value
            Operator = $match.Groups["operator"].Value
            AssignedExpression = $match.Groups["value"].Value.Trim()
            RuntimePath = "UNKNOWN"
            TransitionGuard = "UNKNOWN"
            FindingIds = ""
        }
    }
}

$outputs = @(
    Write-CsvUtf8 -Rows $actionRows -Name "CONTROLLER_ACTION_INVENTORY.csv"
    Write-CsvUtf8 -Rows $navigationRows -Name "UI_NAVIGATION_INVENTORY.csv"
    Write-CsvUtf8 -Rows $serviceRows -Name "SERVICE_REGISTRATION_INVENTORY.csv"
    Write-CsvUtf8 -Rows $dbSetRows -Name "DBSET_INVENTORY.csv"
    Write-CsvUtf8 -Rows $writeRows -Name "INVENTORY_WRITE_CANDIDATES.csv"
    Write-CsvUtf8 -Rows $stateRows -Name "STATE_MUTATION_CANDIDATES.csv"
)

Write-Output "Controller actions: $($actionRows.Count)"
Write-Output "Navigation links:   $($navigationRows.Count)"
Write-Output "DI registrations:   $($serviceRows.Count)"
Write-Output "DbSets:             $($dbSetRows.Count)"
Write-Output "Inventory writes:   $($writeRows.Count)"
Write-Output "State mutations:    $($stateRows.Count)"
Write-Output "Artifacts:"
$outputs | ForEach-Object { Write-Output "- $_" }
