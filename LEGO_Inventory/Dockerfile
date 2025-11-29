FROM mcr.microsoft.com/dotnet/sdk:10.0 AS base
USER $APP_UID
ENV PATH="${PATH}:/root/.dotnet/tools"
RUN dotnet tool install --global dotnet-ef --create-manifest-if-needed
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["LEGO_Inventory.csproj", "./"]
RUN dotnet restore "LEGO_Inventory.csproj"
COPY . .
RUN dotnet build "LEGO_Inventory.csproj" -c $BUILD_CONFIGURATION -o /app/build


FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "LEGO_Inventory.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false
WORKDIR /app
RUN cp -r /src/Migrations .

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY --from=publish /app/Migrations .

ENTRYPOINT ["dotnet", "LEGO_Inventory.dll"]