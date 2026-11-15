param(
    [string]$BaseUrl = "http://127.0.0.1:5070",
    [int]$Concurrency = 10,
    [int]$Duration = 10,
    [int]$Warmup = 2,
    [string]$Sku = "WIDGET-1",
    [string]$Strategies = "per-request,factory,long-lived"
)

$ErrorActionPreference = "Stop"

if ($Concurrency -lt 1 -or $Concurrency -gt 50) {
    throw "Concurrency must be from 1 through 50."
}

if ($Duration -lt 1 -or $Duration -gt 30) {
    throw "Duration must be from 1 through 30 seconds."
}

if ($Warmup -lt 0 -or $Warmup -gt 10) {
    throw "Warmup must be from 0 through 10 seconds."
}

$uri = [Uri]$BaseUrl
if (-not $uri.IsLoopback -or ($uri.Scheme -ne "http" -and $uri.Scheme -ne "https")) {
    throw "Load tests are limited to loopback http or https URLs."
}

$project = Join-Path $PSScriptRoot "..\tools\LoadGenerator\LoadGenerator.csproj"
dotnet run --project $project -c Release -- `
    --base-url $BaseUrl `
    --concurrency $Concurrency `
    --duration $Duration `
    --warmup $Warmup `
    --sku $Sku `
    --strategies $Strategies

exit $LASTEXITCODE
