FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY . .
RUN dotnet restore quantum-extension-market/src/Quantum.ExtensionMarket/Quantum.ExtensionMarket.csproj
RUN dotnet publish quantum-extension-market/src/Quantum.ExtensionMarket/Quantum.ExtensionMarket.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
RUN mkdir -p /app/Files && chown -R $APP_UID:$APP_UID /app
USER $APP_UID
ENTRYPOINT ["dotnet", "Quantum.ExtensionMarket.dll"]
