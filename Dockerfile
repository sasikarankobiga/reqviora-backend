FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore Reqviora.sln \
    && dotnet publish src/Reqviora.Api/Reqviora.Api.csproj -c Release --no-restore \
        --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
    && apt-get install --yes --no-install-recommends ca-certificates curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /var/lib/reqviora/private-artifacts /var/lib/reqviora/data-protection-keys \
    && chown -R "$APP_UID:$APP_UID" /app /var/lib/reqviora
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl --fail --silent --show-error http://127.0.0.1:8080/health/live || exit 1
ENTRYPOINT ["dotnet", "Reqviora.Api.dll"]
