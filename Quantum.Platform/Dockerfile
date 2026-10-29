FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY NuGet.config ./
COPY Quantum.Platform/ ./Quantum.Platform/
COPY Quantum/Directory.Build.props Quantum/Directory.Packages.props ./Quantum/
COPY Quantum/quantum/src/Quantum.Mobile.Internal/ ./Quantum/quantum/src/Quantum.Mobile.Internal/
RUN dotnet restore Quantum.Platform/src/Quantum.Platform/Quantum.Platform.csproj
RUN dotnet publish Quantum.Platform/src/Quantum.Platform/Quantum.Platform.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# Configuration is injected by the service config resource, never baked into runtime layers.
RUN find /app/publish -maxdepth 1 -type f -name 'appsettings*.json' -delete

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
USER $APP_UID
HEALTHCHECK --interval=10s --timeout=3s --start-period=30s --retries=6 \
    CMD curl --fail --silent --show-error http://127.0.0.1:8080/health/live >/dev/null || exit 1
ENTRYPOINT ["dotnet", "Quantum.Platform.dll"]
