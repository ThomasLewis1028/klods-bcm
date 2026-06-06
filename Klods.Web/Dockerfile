FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
RUN mkdir -p /src /app && chown app:app /src /app
USER app
WORKDIR /src
COPY --chown=app:app ["Klods.Core/Klods.Core.csproj", "Klods.Core/"]
COPY --chown=app:app ["Klods.Web/Klods.Web.csproj", "Klods.Web/"]
RUN dotnet restore "Klods.Web/Klods.Web.csproj"
COPY --chown=app:app Klods.Core/ Klods.Core/
COPY --chown=app:app Klods.Web/ Klods.Web/
RUN dotnet build "Klods.Web/Klods.Web.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Klods.Web/Klods.Web.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false && \
    rm -f /app/publish/Klods.Web.staticwebassets.runtime.json

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
USER app
EXPOSE 8080
EXPOSE 8081
ENTRYPOINT ["dotnet", "Klods.Web.dll"]
