# Klods - A Brick Collection Manager — Claude Instructions

## Project Tracking
- Issues and planning articles for this repo are tracked in YouTrack project **KBCM**.

## Secrets & Sensitive Files
- **Never read `.env` files.** If understanding config is needed, ask the user to describe the relevant variables.
- Never read or suggest committing `appsettings.Development.json` if it contains secrets.
- Never log, print, or echo secret values even in debug/test code.

## .NET Conventions
- Target framework is **net10.0**; use current C# language features (pattern matching, records, primary constructors, etc.).
- Nullable reference types are enabled — never suppress with `!` unless unavoidable; fix the root cause instead.
- Use `async`/`await` throughout; never use `.Result` or `.Wait()` on tasks.
- Prefer `IConfiguration` / environment variables for config; never hardcode connection strings or secrets.
- Use EF Core conventions and data annotations before reaching for raw SQL.
- Run `dotnet ef migrations add <Name>` then `dotnet ef database update` for schema changes — never modify migration files by hand.
- Prefer `var` when the type is obvious from the right-hand side.

## Solution Structure
- Solution file: `Klods.slnx` at the repo root
- `Klods.Core/` — class library; all EF models, InventoryContext, migrations, ImportData, UpdateData, DeleteData, RebrickableApi, ImageStorageService, InventoryAggregates, ColorHelper
- `Klods.Web/` — Blazor Server app; references Core; contains all Components, AuthService, PendingAuthService, ThemeService
- `Klods.Api/` — ASP.NET Core Web API; references Core; JWT auth; minimal API endpoints
- EF migrations live in `Klods.Core/Migrations/`. Run: `dotnet ef migrations add <Name> --project Klods.Core`

## Blazor / MudBlazor
- This is a **Blazor Server** app — avoid patterns that only work in Blazor WebAssembly.
- MudBlazor version is **8.15.0** — `MudColorPicker` does not exist; use `<input type="color">` + `MudTextField` instead.
- Use `IMudDialogInstance` (not `MudDialog`) for dialog cascading parameters.
- `AuthService` is Scoped (one per circuit); `PendingAuthService` is Singleton — respect these lifetimes when injecting services.

## Docker / Deployment
- The app runs via Docker Compose (`compose.yaml`). Port mapping: app → 8080, pgAdmin → 8888, postgres → 5432.
- Environment variables are injected via `.env` at compose time — do not duplicate them in `appsettings.json`.

## Testing
- Test project uses MSTest. Keep tests in `InventoryTests.cs` unless a new file is clearly warranted.
- Do not mock the database unless integration against a real DB is impossible for the scenario.

## General
- Prefer editing existing files over creating new ones.
- Do not add comments to code that is already self-explanatory.
- Do not add error handling for scenarios that cannot realistically occur.
- Keep solutions simple — don't design for hypothetical future requirements.
