# Krypto-Steuer

Selbst gehostete Alternative zu CoinTracking für eine einzelne Person in Deutschland: Importe
(Bitget, Trade Republic, weitere Broker), Steuerberechnung nach deutschem Recht und ein
nachvollziehbarer Jahresreport. Fokus Krypto; Aktien und ETFs folgen später.

> **Haftungshinweis: Keine Steuerberatung.** Die App bereitet Zahlen und Belege auf. Sonderfälle
> (DeFi, Mining, Futures-Verluste u. a.) sollte eine Steuerberaterin prüfen, bevor du abgibst.

## Stand

P1 bis P5 sind umgesetzt: Projektgerüst, Datenmodell, CI, Import-Framework und Bitget-API-Anbindung (siehe `docs/04-importe.md`).
Es gibt noch keine Steuerlogik, keine CSV-Importer für Bitget und Trade Republic und keine Oberfläche für Importe. Der Fahrplan steht im Prompt-Katalog.

## Voraussetzungen

| Werkzeug | Version |
|---|---|
| .NET SDK | 10 (LTS) |
| Node.js | `^22.22.3`, `^24.15` oder `^26` (siehe `src/frontend/.nvmrc`) |
| Docker + Compose | aktuell; Testcontainers braucht einen laufenden Docker-Daemon |

## Schnellstart (alles in Docker)

```bash
cp .env.example .env        # optional, es gibt lokale Defaults
docker compose up --build
```

| Dienst | Adresse |
|---|---|
| Oberfläche | http://localhost:4200 |
| API | http://localhost:5080/api/health |
| OpenAPI-Spezifikation | http://localhost:5080/openapi/v1.json |
| PostgreSQL | `127.0.0.1:5432` |
| pgAdmin (optional) | http://localhost:5050, Start mit `docker compose --profile tools up` |

Alle Ports sind nur an `127.0.0.1` gebunden.

## Entwicklung ohne Docker für API und Frontend

```bash
docker compose up -d postgres                 # nur die Datenbank
dotnet run --project src/backend/Api          # API auf der in der Konsole genannten Adresse
cd src/frontend && npm install && npm start   # Frontend mit Proxy auf http://localhost:5080
```

Für den Proxy muss die API auf Port 5080 laufen (`--urls http://localhost:5080`).

## Tests

```bash
dotnet test src/backend/KryptoSteuer.sln      # Integrationstests starten PostgreSQL per Testcontainers
cd src/frontend && npm test                   # Vitest über den Angular-Builder
```

## API-Client generieren

Die API veröffentlicht ihre OpenAPI-Spezifikation unter `/openapi/v1.json`. Der Client entsteht daraus:

```bash
cd src/frontend
npm install --save-dev @hey-api/openapi-ts
# API läuft auf :5080
npm run generate:api
```

Bis dahin nutzt die Startseite den handgeschriebenen Dienst `src/app/api/health-api.ts`.

## Aufbau

Siehe [docs/02-architektur.md](docs/02-architektur.md) und
[ADR 0001](docs/adr/0001-modularer-monolith.md). Regeln für den Coding-Agenten stehen in
[CLAUDE.md](CLAUDE.md).
