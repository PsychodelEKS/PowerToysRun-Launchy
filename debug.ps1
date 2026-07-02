param(
    [string]$Platform = "x64",
    [string]$Configuration = "Debug",
    [string]$DotnetExe = "dotnet"
)

$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot

$project = ".\Community.PowerToys.Run.Plugin.Launchy\Community.PowerToys.Run.Plugin.Launchy.csproj"
$target = Join-Path $env:LOCALAPPDATA "Microsoft\PowerToys\PowerToys Run\Plugins\Launchy"
$source = ".\Community.PowerToys.Run.Plugin.Launchy\bin\$Platform\$Configuration\net10.0-windows"

& $DotnetExe build $project -c $Configuration -p:Platform=$Platform
Remove-Item $target -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $target | Out-Null

Copy-Item "$source\Community.PowerToys.Run.Plugin.Launchy.dll" $target -Force
Copy-Item "$source\Community.PowerToys.Run.Plugin.Launchy.deps.json" $target -Force
Copy-Item "$source\Community.PowerToys.Run.Plugin.Launchy.runtimeconfig.json" $target -Force
Copy-Item "$source\plugin.json" $target -Force
Copy-Item "$source\Images" $target -Recurse -Force

Pop-Location
