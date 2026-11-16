param(
    [string]$PgRoot = "C:\Program Files\PostgreSQL\17",
    [string]$PgVectorTag = "v0.8.0"
)

$ErrorActionPreference = "Stop"
Write-Host "[VelitIQ] Building & Installing Official pgvector ($PgVectorTag) into $PgRoot..." -ForegroundColor Cyan
Write-Host "Prerequisite: Run inside 'x64 Native Tools Command Prompt for VS 2022' as Administrator." -ForegroundColor Yellow

$env:PGROOT = $PgRoot
$BuildDir = Join-Path $env:TEMP "velitiq_pgvector_build"
if (Test-Path $BuildDir) { Remove-Item -Recurse -Force $BuildDir }

git clone --branch $PgVectorTag --depth 1 https://github.com/pgvector/pgvector.git $BuildDir
Push-Location $BuildDir
try {
    & nmake /F Makefile.win
    & nmake /F Makefile.win install
    Write-Host "[VelitIQ] Successfully installed vector.dll & SQL files into $PgRoot!" -ForegroundColor Green
    Write-Host "Restart PostgreSQL service; VelitIQ will automatically detect pgvector and enable HNSW indexing." -ForegroundColor Green
}
finally {
    Pop-Location
}
