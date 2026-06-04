FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
RUN mkdir -p /src /app && chown app:app /src /app
USER app
WORKDIR /src
COPY --chown=app:app ["LEGO_Inventory.Core/LEGO_Inventory.Core.csproj", "LEGO_Inventory.Core/"]
COPY --chown=app:app ["LEGO_Inventory.Api/LEGO_Inventory.Api.csproj", "LEGO_Inventory.Api/"]
RUN dotnet restore "LEGO_Inventory.Api/LEGO_Inventory.Api.csproj"
COPY --chown=app:app LEGO_Inventory.Core/ LEGO_Inventory.Core/
COPY --chown=app:app LEGO_Inventory.Api/ LEGO_Inventory.Api/
RUN dotnet publish "LEGO_Inventory.Api/LEGO_Inventory.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false && \
    rm -f /app/publish/LEGO_Inventory.Api.staticwebassets.runtime.json

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "LEGO_Inventory.Api.dll"]
