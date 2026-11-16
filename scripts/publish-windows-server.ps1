param(
    [string]$OutputDir = "$PSScriptRoot\..\publish"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path "$PSScriptRoot\.."
$Dotnet = if (Test-Path "C:\Program Files\dotnet\dotnet.exe") { "C:\Program Files\dotnet\dotnet.exe" } else { "dotnet" }

Write-Host "[VelitIQ] Publishing .NET 10 API + McpHttpServer for Windows Server x64..." -ForegroundColor Cyan
& $Dotnet publish "$RepoRoot\backend\src\VelitIQ.Api\VelitIQ.Api.csproj" -c Release -r win-x64 --self-contained false -o "$OutputDir\VelitIQ.Api"

Write-Host "[VelitIQ] Publishing Companion STDIO MCP Server (Keerukee.MCPServer.Stdio)..." -ForegroundColor Cyan
& $Dotnet publish "$RepoRoot\backend\src\VelitIQ.Mcp.Cli\VelitIQ.Mcp.Cli.csproj" -c Release -r win-x64 --self-contained false -o "$OutputDir\VelitIQ.Mcp.Cli"

Write-Host "[VelitIQ] Building Next.js Production Frontend..." -ForegroundColor Cyan
Push-Location "$RepoRoot\frontend"
try {
    npm ci
    npm run build
}
finally {
    Pop-Location
}

Write-Host "[VelitIQ] Windows Server deployment artifacts ready under: $OutputDir" -ForegroundColor Green
