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

## Comments & Documentation

See **Text rules** below — the single authority for what a comment may say and what
must never appear in tracked text.

## Coding Style & Naming Conventions

Use C# with four-space indentation, nullable reference types enabled, and explicit `using` directives. Use `PascalCase` for public types and members, `camelCase` for locals and private fields, and stable lowercase action IDs such as `mod-id:toggle-vision`. Keep public API types small and immutable where practical. Unity object access and callbacks must remain on the main thread. Avoid broad refactors in compatibility-sensitive UI code.

## Testing Guidelines

Add contract checks for every public API or persistence change. Name checks by observable behavior, not implementation detail. Keep source-layout assertions separate from behavioral tests. Always run the Release build and contract suite. For focus, scene, input, or UI changes, also test in Sprocket `0.2.53.2`; a successful build does not prove runtime acceptance.

## Commit & Pull Request Guidelines

The current history uses concise release subjects (`Release v0.1.0`). Use short imperative subjects for normal work, for example `Fix focus rearm state`, and keep each commit logically focused. Pull requests should explain behavior and compatibility impact, list exact validation commands, and distinguish offline checks from in-game results. Include screenshots for UI changes and note the tested game, MelonLoader, and API versions. Never commit game binaries, generated `bin/` or `obj/` output, logs, or deployed DLLs.

## Text rules

Every tracked file — code comments, docstrings, docs, tests, release notes, commit
messages — obeys these. They outrank convenience.

1. **Current state only.** Describe what the code is now, plus the delta since the
   last release. Never narrate development: no "previously X, now Y", "no longer",
   "was removed", "temporary", no "曾经/原来的/此前/第一版/中间状态/过渡". If a sentence exists
   only to explain that something is gone, state the current rule instead.

   A delta is the **net** difference between the last release and now — only what is
   still true. If the last release had {a, b} and the work removed a, added c, removed
   b, restored a and removed c, the current state is {a} and the only reportable change
   is "b was removed". Never mention c, and never mention a's removal or restoration:
   a change that later undid itself is not a change.
2. **Nonexistent things stay absent everywhere.** Do not mention them, do not handle
   them defensively, and do not test for their absence — delete a check whose point
   is that something was removed or must not exist (including checks that some file
   or document does not exist). Keep a negative assertion only when the absence is
   itself a current invariant: no `.tmp` left after a write, a corrupted file is
   backed up then reset, unknown keys are preserved.
3. **No attribution.** Never "用户要求…", "the user asked for…", "per request" — say what
   the code does and why it must stay that way; the request is not part of the artifact.
4. **Comments earn their place.** Plain `//` only: no `///` XML tags. These projects
   set no `GenerateDocumentationFile`, so tags produce no artifact and only force
   escaping `<` inside comments. Comment only contracts invisible from the signature — priority
   order, invariants, threading, on-disk formats, why a fallback exists. Never one
   per member, never restate a name or a formula.
5. **Compatibility follows the same net delta.** Only a removal that survives into
   the current state (`b` in rule 1) can create a compatibility obligation; something
   that was never released, or that was changed and changed back, needs neither
   compatibility nor migration. Never add migrations, dual-format readers or legacy
   fallbacks for data the current code cannot read.
6. **Versions and release notes change only when authorised.** Never bump the mod
   version or the public API version, and never retitle release notes, on your own.
   Release notes are the only place for a delta, and only for an authorised release.
7. **Cleaning text is semantic work.** A script may extract raw text; it must never
   decide what counts as narration — no keyword, regex or pattern matching. Read and
   judge. When a case is ambiguous, leave it alone and list it for the user's review.
8. **Do not expand documentation on your own initiative**, and never substitute a
   comment for a behaviour change.
