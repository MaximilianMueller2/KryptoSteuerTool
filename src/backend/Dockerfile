# Build-Kontext ist das Repo-Root: docker build -f src/backend/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Erst nur die Projektdateien kopieren, damit der Restore-Layer gecacht wird.
COPY Directory.Build.props ./
COPY src/backend/Domain/*.csproj src/backend/Domain/
COPY src/backend/Application/*.csproj src/backend/Application/
COPY src/backend/Infrastructure/*.csproj src/backend/Infrastructure/
COPY src/backend/Importers/*.csproj src/backend/Importers/
COPY src/backend/TaxEngine/*.csproj src/backend/TaxEngine/
COPY src/backend/Api/*.csproj src/backend/Api/
RUN dotnet restore src/backend/Api/KryptoSteuer.Api.csproj

COPY src/backend/ src/backend/
RUN dotnet publish src/backend/Api/KryptoSteuer.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# curl für den Compose-Healthcheck
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
# Schlüsselring-Volume (Data Protection) muss für den unprivilegierten Benutzer beschreibbar sein.
RUN mkdir /keys && chown $APP_UID /keys
USER $APP_UID
ENTRYPOINT ["dotnet", "KryptoSteuer.Api.dll"]
