FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
ARG VERSION=0.0.0-dev
RUN mkdir -p /src /app && chown app:app /src /app
USER app
WORKDIR /src
COPY --chown=app:app ["Klods.Mcp/Klods.Mcp.csproj", "Klods.Mcp/"]
RUN dotnet restore "Klods.Mcp/Klods.Mcp.csproj"
COPY --chown=app:app Klods.Mcp/ Klods.Mcp/
RUN dotnet publish "Klods.Mcp/Klods.Mcp.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false /p:Version=$VERSION && \
    rm -f /app/publish/Klods.Mcp.staticwebassets.runtime.json

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
# curl for the compose healthcheck (the slim base image ships without it).
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Klods.Mcp.dll"]
