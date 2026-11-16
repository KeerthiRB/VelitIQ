param(
    [string]$PgRoot = "$env:USERPROFILE\.velitiq-pg17",
    [int]$Port = 5432,
    [switch]$InitOnly
)

$ErrorActionPreference = "Stop"
$ZipUrl = "https://get.enterprisedb.com/postgresql/postgresql-17.4-1-windows-x64-binaries.zip"
$ZipPath = Join-Path $env:TEMP "postgresql-17.4-1-windows-x64-binaries.zip"
$PgsqlDir = Join-Path $PgRoot "pgsql"
$DataDir = Join-Path $PgRoot "data"
$BinDir = Join-Path $PgsqlDir "bin"

if (-not (Test-Path (Join-Path $BinDir "postgres.exe"))) {
    New-Item -ItemType Directory -Force -Path $PgRoot | Out-Null
    if (-not (Test-Path $ZipPath)) {
        Write-Host "[VelitIQ] Downloading official PostgreSQL 17.4 Windows x64 binaries..." -ForegroundColor Cyan
        & curl.exe -L --retry 3 -o $ZipPath $ZipUrl
    }
    Write-Host "[VelitIQ] Extracting PostgreSQL 17.4 binaries to $PgRoot..." -ForegroundColor Cyan
    & tar.exe -xf $ZipPath -C $PgRoot
}

if (-not (Test-Path (Join-Path $DataDir "PG_VERSION"))) {
    Write-Host "[VelitIQ] Initializing PostgreSQL 17 data directory at $DataDir..." -ForegroundColor Cyan
    & (Join-Path $BinDir "initdb.exe") -D $DataDir -U postgres -A trust -E UTF8 --locale=C
    Add-Content -Path (Join-Path $DataDir "postgresql.conf") -Value "`nport = $Port`nmax_connections = 100`nshared_buffers = 256MB`n"
}

if ($InitOnly) {
    Write-Host "[VelitIQ] PostgreSQL 17 binaries & data directory ready at $PgRoot." -ForegroundColor Green
    exit 0
}

$portOpen = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
if (-not $portOpen) {
    $LogFile = Join-Path $PgRoot "postgres.log"
    Write-Host "[VelitIQ] Starting PostgreSQL 17 on port $Port..." -ForegroundColor Cyan
    & (Join-Path $BinDir "pg_ctl.exe") -D $DataDir -l $LogFile -w start
}

& (Join-Path $BinDir "psql.exe") -h localhost -p $Port -U postgres -tc "SELECT 1 FROM pg_database WHERE datname = 'velitiq'" | Select-String "1" -Quiet | Out-Null
if ($LASTEXITCODE -ne 0) {
    & (Join-Path $BinDir "createdb.exe") -h localhost -p $Port -U postgres velitiq
    Write-Host "[VelitIQ] Created database 'velitiq'." -ForegroundColor Green
}

& (Join-Path $BinDir "psql.exe") -h localhost -p $Port -U postgres -tc "SELECT 1 FROM pg_database WHERE datname = 'velitiq_test'" | Select-String "1" -Quiet | Out-Null
if ($LASTEXITCODE -ne 0) {
    & (Join-Path $BinDir "createdb.exe") -h localhost -p $Port -U postgres velitiq_test
    Write-Host "[VelitIQ] Created database 'velitiq_test'." -ForegroundColor Green
}

Write-Host "[VelitIQ] PostgreSQL 17 is running on localhost:$Port with databases 'velitiq' and 'velitiq_test'." -ForegroundColor Green
