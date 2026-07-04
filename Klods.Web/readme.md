# Configuration

Copy [`.env.example`](../.env.example) to `.env` in the repo root and fill in the values.
`.env` is git-ignored — **do not commit it.** See the comments in `.env.example` for what
each variable does and which are required.

For local IDE runs (F5 / `dotnet run`) that don't load `.env`, supply
`REBRICKABLE_API_KEY` via your shell environment or .NET user-secrets rather than
hardcoding it in `launchSettings.json`.

# Setting up / updating the database
Prereq: `dotnet tool install --global dotnet-ef`
1. Change to the `Klods.Core` project folder.
2. `dotnet ef migrations add <MigrationName>` — the name of the new migration.
3. `dotnet ef database update`

# Removing a migration
1. `dotnet ef migrations remove`
