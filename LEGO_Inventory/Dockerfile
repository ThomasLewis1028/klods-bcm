FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
RUN mkdir -p /src /app && chown app:app /src /app
USER app
WORKDIR /src
COPY --chown=app:app ["LEGO_Inventory.Core/LEGO_Inventory.Core.csproj", "LEGO_Inventory.Core/"]
COPY --chown=app:app ["LEGO_Inventory/LEGO_Inventory.csproj", "LEGO_Inventory/"]
RUN dotnet restore "LEGO_Inventory/LEGO_Inventory.csproj"
COPY --chown=app:app LEGO_Inventory.Core/ LEGO_Inventory.Core/
COPY --chown=app:app LEGO_Inventory/ LEGO_Inventory/
RUN dotnet build "LEGO_Inventory/LEGO_Inventory.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "LEGO_Inventory/LEGO_Inventory.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false && \
    rm -f /app/publish/LEGO_Inventory.staticwebassets.runtime.json

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
USER app
EXPOSE 8080
EXPOSE 8081
ENTRYPOINT ["dotnet", "LEGO_Inventory.dll"]
