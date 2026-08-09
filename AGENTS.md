# Repository Guidelines

## Project Structure & Module Organization

`SprocketModAPI/` contains the net6 MelonLoader assembly. `Core/` owns only the public entry point, service registry, and common runtime-module lifecycle. `Modules/Keybindings/` owns all keybinding contracts, routing, persistence, management UI, and Settings observers. Future modules belong in their own `Modules/<Name>/` directory, may depend on Core, and must not depend on another module's internals. No game-native UI module exists yet; do not describe hand-built Unity UGUI as a Sprocket UI component service. `SprocketModAPI.ContractTests/` is a console-based offline contract suite. User documentation belongs in `docs/`, while release-facing changes belong in `README.md` and `RELEASE_NOTES.md`. `TODO.md` is a local, Git-ignored planning file and must not be staged.

## Build, Test, and Development Commands

Run commands from the repository root in PowerShell:

```powershell
dotnet build .\SprocketModAPI\SprocketModAPI.csproj --configuration Release -p:SkipModDeploy=true
dotnet run --configuration Release --project .\SprocketModAPI.ContractTests\SprocketModAPI.ContractTests.csproj
```

The build expects Sprocket and MelonLoader assemblies under `G:\Sprocket` by default. Use `-p:SprocketGameRoot="D:\Games\Sprocket"` for another installation. Omitting `SkipModDeploy` copies the DLL into the game's `Mods` directory; do this only for deliberate in-game testing.

## Coding Style & Naming Conventions

Use C# with four-space indentation, nullable reference types enabled, and explicit `using` directives. Use `PascalCase` for public types and members, `camelCase` for locals and private fields, and stable lowercase action IDs such as `mod-id:toggle-vision`. Keep public API types small and immutable where practical. Unity object access and callbacks must remain on the main thread. Avoid broad refactors in compatibility-sensitive UI code.

## Testing Guidelines

Add contract checks for every public API or persistence change. Name checks by observable behavior, not implementation detail. Keep source-layout assertions separate from behavioral tests. Always run the Release build and contract suite. For focus, scene, input, or UI changes, also test in Sprocket `0.2.53.2`; a successful build does not prove runtime acceptance.

## Commit & Pull Request Guidelines

The current history uses concise release subjects (`Release v0.1.0`). Use short imperative subjects for normal work, for example `Fix focus rearm state`, and keep each commit logically focused. Pull requests should explain behavior and compatibility impact, list exact validation commands, and distinguish offline checks from in-game results. Include screenshots for UI changes and note the tested game, MelonLoader, and API versions. Never commit game binaries, generated `bin/` or `obj/` output, logs, or deployed DLLs.
