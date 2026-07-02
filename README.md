# PowerToys Run Launchy

Launchy-style folder index plugin for PowerToys Run.

## Features

- Index files from folders you choose.
- Configure extensions, traversal depth, and whether folders should be included per indexed folder.
- Use `ln <query>` to search indexed entries.
- Use `ln rescan` to rebuild the index from PowerToys Run.
- Optionally expose indexed entries in global PowerToys Run results.

## Install with ptr

Install [`ptr`](https://github.com/8LWXpg/ptr), then run:

```powershell
ptr add Launchy PsychodelEKS/PowerToysRun-Launchy
```

## Manual install

1. Download the latest `PowerToysRun-Launchy-*-x64.zip` or `PowerToysRun-Launchy-*-arm64.zip` from the [releases page](https://github.com/PsychodelEKS/PowerToysRun-Launchy/releases).
2. Exit PowerToys completely.
3. Create this folder if it does not exist:

```powershell
$env:LOCALAPPDATA\Microsoft\PowerToys\PowerToys Run\Plugins\Launchy
```

4. Extract the zip contents directly into that folder. `plugin.json` should be at:

```powershell
$env:LOCALAPPDATA\Microsoft\PowerToys\PowerToys Run\Plugins\Launchy\plugin.json
```

5. Start PowerToys again.

## Build

```powershell
dotnet build .\PowerToysRun-Launchy.sln -c Release -p:Platform=x64
```

This plugin was bootstrapped from the [PowerToysRun Plugin Template](https://github.com/8LWXpg/PowerToysRun-PluginTemplate).

## Release

Create and push a version tag:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

GitHub Actions will publish `x64` and `arm64` zip assets compatible with `ptr`.
