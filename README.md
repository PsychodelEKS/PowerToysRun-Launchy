# PowerToys Run Launchy

Launchy-style folder index plugin for PowerToys Run.

## Features

- Index files from folders you choose.
- Configure extensions, traversal depth, and whether folders should be included per indexed folder.
- Use `ln <query>` to search indexed entries.
- Use `ln settings` to edit folder rules with a table and folder picker.
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

## Settings

Open PowerToys Settings, go to PowerToys Run plugins, then open `Launchy`.

Use `Folder rules` to see and edit indexed folders. You can also run `ln settings` in PowerToys Run to edit the same list with a table and folder picker.

The text field uses one rule per line:

```text
path | extensions | maxDepth | includeDirectories | enabled
```

Example:

```text
C:\Tools | .exe;.lnk | 10 | false | true
D:\PortableApps | .exe;.bat;.cmd | 2 | true | true
```

`extensions`, `maxDepth`, `includeDirectories`, and `enabled` are optional. Defaults are `.exe;.lnk`, `10`, `false`, and `true`.

## Build

```powershell
dotnet build .\PowerToysRun-Launchy.sln -c Release -p:Platform=x64
```

This plugin was bootstrapped from the [PowerToysRun Plugin Template](https://github.com/8LWXpg/PowerToysRun-PluginTemplate).

## Release

Create and push a version tag:

```powershell
git tag vX.Y.Z
git push origin vX.Y.Z
```

GitHub Actions will publish `x64` and `arm64` zip assets compatible with `ptr`.
