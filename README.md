# PowerToys Run Launchy

Launchy-style folder index plugin for PowerToys Run.

## Features

- Index files from folders you choose.
- Configure extensions, traversal depth, and whether folders should be included per indexed folder.
- Use `ln <query>` to search indexed entries.
- Use `ln rescan` to rebuild the index from PowerToys Run.
- Optionally expose indexed entries in global PowerToys Run results.

## Install with ptr

```powershell
ptr add Launchy PsychodelEKS/PowerToysRun-Launchy
```

## Build

```powershell
dotnet build .\PowerToysRun-Launchy.sln -c Release -p:Platform=x64
```

## Release

Create and push a version tag:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

GitHub Actions will publish `x64` and `arm64` zip assets compatible with `ptr`.

