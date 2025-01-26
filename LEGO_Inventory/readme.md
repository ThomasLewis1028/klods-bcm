

# Setting up/Updating the Database
prereg - dotnet tool install --global dotnet-ef
1. Change current directory to the `SWNUniverseGenerator` project folder.
2. Run `dotnet ef migrations add <migrationName>` where `<migrationName>` is the name of the current migration.
3. Run `dotnet ef database update`.