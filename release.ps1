param(
    [string]$Configuration = "Release",
    [string[]]$Platforms = @("x64", "ARM64"),
    [string]$DotnetExe = "dotnet"
)

$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot

$project = ".\Community.PowerToys.Run.Plugin.Launchy\Community.PowerToys.Run.Plugin.Launchy.csproj"
$pluginJson = Get-Content ".\Community.PowerToys.Run.Plugin.Launchy\plugin.json" | ConvertFrom-Json
$version = "v$($pluginJson.Version)"
$outDir = ".\out"

Remove-Item $outDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $outDir | Out-Null

foreach ($platform in $Platforms) {
    & $DotnetExe build $project -c $Configuration -p:Platform=$platform

    $releasePath = ".\Community.PowerToys.Run.Plugin.Launchy\bin\$platform\$Configuration\net10.0-windows"
    $packageRoot = Join-Path $outDir "Launchy"
    Remove-Item $packageRoot -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $packageRoot | Out-Null

    Copy-Item "$releasePath\Community.PowerToys.Run.Plugin.Launchy.dll" $packageRoot -Force
    Copy-Item "$releasePath\Community.PowerToys.Run.Plugin.Launchy.deps.json" $packageRoot -Force
    Copy-Item "$releasePath\Community.PowerToys.Run.Plugin.Launchy.runtimeconfig.json" $packageRoot -Force
    Copy-Item "$releasePath\plugin.json" $packageRoot -Force
    Copy-Item "$releasePath\Images" $packageRoot -Recurse -Force

    $assetPlatform = if ($platform -eq "ARM64") { "arm64" } else { "x64" }
    $zipPath = Join-Path $outDir "PowerToysRun-Launchy-$version-$assetPlatform.zip"
    Compress-Archive -Path "$packageRoot\*" -DestinationPath $zipPath -Force
}

Pop-Location
