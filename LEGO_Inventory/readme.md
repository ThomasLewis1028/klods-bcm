# Requires .env file in the folder with the following environment variables
## DO NOT CHECK IN ##
```
LEGO_API_KEY=<your_api_key> # The API key to connect to the Lego API 
POSTGRES_HOST=postgres # The PostgreSQL database host URL (Leave as postgres for docker or change to local instance for http run)
POSTGRES_USER=<your_username> # The PostgreSQL database user
POSTGRES_PASSWORD=<Your_password> # The PostgreSQL database password
POSTGRES_DB=lego_database # The PostgreSQL database name
PGADMIN_DEFAULT_EMAIL=<your_email> #PostgresSQL Web Admin username
PGADMIN_DEFAULT_PASSWORD=<your_password>  #PostgresSQL Web Admin password
```

# Setting up/Updating the Database
prereq: dotnet tool install --global dotnet-ef
1. Change current directory to the `LEGO_Inventory` project folder.
2. Run `dotnet ef migrations add <migrationName>` where `<migrationName>` is the name of the current migration.
3. Run `dotnet ef database update`.

# Remove datadase Migration
1. Run `dotnet ef migrations remove <migrationName>`