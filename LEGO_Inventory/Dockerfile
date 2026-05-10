FROM mcr.microsoft.com/dotnet/sdk:10.0 AS base
ENV PATH="${PATH}:/root/.dotnet/tools"
RUN dotnet tool install --global dotnet-ef --create-manifest-if-needed
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["LEGO_Inventory.Core/LEGO_Inventory.Core.csproj", "LEGO_Inventory.Core/"]
COPY ["LEGO_Inventory/LEGO_Inventory.csproj", "LEGO_Inventory/"]
RUN dotnet restore "LEGO_Inventory/LEGO_Inventory.csproj"
COPY LEGO_Inventory.Core/ LEGO_Inventory.Core/
COPY LEGO_Inventory/ LEGO_Inventory/
RUN dotnet build "LEGO_Inventory/LEGO_Inventory.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "LEGO_Inventory/LEGO_Inventory.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "LEGO_Inventory.dll"]
