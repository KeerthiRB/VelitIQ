$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path "$PSScriptRoot\.."
$Dotnet = if (Test-Path "C:\Program Files\dotnet\dotnet.exe") { "C:\Program Files\dotnet\dotnet.exe" } else { "dotnet" }

Write-Host "1/3 Ensuring PostgreSQL 17 is running on localhost:5432..." -ForegroundColor Cyan
powershell -ExecutionPolicy Bypass -File "$RepoRoot\scripts\setup-local-postgres.ps1"

Write-Host "2/3 Starting VelitIQ .NET 10 API + McpHttpServer on http://127.0.0.1:5180 ..." -ForegroundColor Cyan
Start-Process -FilePath $Dotnet -ArgumentList "run --project `"$RepoRoot\backend\src\VelitIQ.Api\VelitIQ.Api.csproj`" --urls http://127.0.0.1:5180" -WorkingDirectory $RepoRoot

Write-Host "3/3 Starting VelitIQ Next.js Portal on http://localhost:3000 ..." -ForegroundColor Cyan
Set-Location "$RepoRoot\frontend"
npm run dev
