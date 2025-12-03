# Repository Guidelines

## Project Structure & Module Organization
- Root WPF app targeting `net10.0-windows`; MVVM via `CommunityToolkit.Mvvm`.
- `App.xaml`, `MainWindow.xaml` host the shell; styles live in `TailwindStyles.xaml`.
- `Models/` defines diff shapes (`DiffResult`, `CellDiff`, etc.).
- `Services/` holds data access (`PostgresClient`, `OracleClient`), diff logic (`DiffService`), presets and logging helpers.
- `ViewModels/` contains binding logic; `Views/` contains user controls/pages; `Data/` keeps DB client helpers.
- `Tests/` includes xUnit specs for services/view-models. Build artifacts stay in `bin/` and `obj/`.

## Build, Test, and Development Commands
- `dotnet restore` — restore NuGet packages.
- `dotnet build diff-table-app.csproj` — compile the WPF app.
- `dotnet run --project diff-table-app.csproj` — launch the UI (Windows only).
- `dotnet test diff-table-app.csproj` — run xUnit suite; use `-l trx` if you need reports.
- Optional: `dotnet format` before commits to keep spacing and using directives clean.

## Coding Style & Naming Conventions
- C# 10 with nullable and implicit usings enabled; stick to 4-space indentation.
- Namespaces follow `diff_table_app`; classes/methods/properties use PascalCase, private fields prefer `_camelCase`.
- Keep MVVM boundaries: state in view-models, UI in XAML, async service calls off the UI thread.
- Prefer explicit column/row handling in diff logic; be mindful of case-insensitive maps and column mappings.
- When adding presets or configs, default paths should resolve under the app base directory (see `PresetService`).

## Testing Guidelines
- Framework: xUnit with `Fact`/`Theory`; tests live beside peers in `Tests/`.
- Naming: `Method_Scenario_ExpectedResult` (see `DiffServiceTests`, `MainViewModelTests`).
- Cover edge cases: column mapping differences, key mismatches, null/DB null handling, and logging fallbacks.
- Keep tests deterministic; avoid external DBs—mock service behaviors and use in-memory tables.

## Commit & Pull Request Guidelines
- Commit history uses conventional-style prefixes (`feat:`, `Fix ...`); prefer `feat:`, `fix:`, `chore:` for clarity.
- Write concise bodies explaining intent and scope; one concern per commit when possible.
- PRs should include: summary of changes, test results (`dotnet test` output), and UI notes/screenshots for visual tweaks.
- Link related issues/requests and call out any breaking changes or migration steps.

## Security & Configuration Tips
- Do not commit secrets or connection strings; use local user secrets or environment variables when wiring DB clients.
- `presets.json` is written next to the executable; keep sample presets sanitized before sharing.
- Avoid committing generated assets from `bin/` and `obj/`; prefer clean outputs on CI or local rebuilds.
