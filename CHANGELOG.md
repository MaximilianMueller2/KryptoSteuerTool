# Changelog

Alle nennenswerten Änderungen werden hier festgehalten.
Format nach [Keep a Changelog](https://keepachangelog.com/de/1.1.0/).

## [Unveröffentlicht]

### Hinzugefügt

- Projektgerüst (P1): Backend-Lösung `KryptoSteuer.sln` mit den Projekten Domain, Application,
  Infrastructure, Importers, TaxEngine und Api (.NET 10).
- Endpunkt `GET /api/health` mit Datenbankprüfung (200/503) und OpenAPI-Spezifikation unter `/openapi/v1.json`.
- Testprojekte Domain.Tests, TaxEngine.Tests, Importers.Tests (Architekturtests) und
  Api.IntegrationTests (Testcontainers für PostgreSQL).
- Angular-App (Standalone, Signals, Material, Routing, i18n Deutsch) mit Startseite, die `/api/health` aufruft.
- `docker-compose.yml` mit `postgres`, `api`, `frontend` (nginx) und optionalem `pgadmin`, jeweils mit Healthchecks.
- Dokumentation: README, `docs/02-architektur.md`, ADR 0001 „Modularer Monolith statt Microservices“.
- `CLAUDE.md` mit Projektregeln und Steuerregeln (Vorgriff auf P0).
- Datenmodell (P2): Ledger-Entitäten (Account, Asset, RawRecord, Transaction/TransactionLeg, TransferLink, Price,
  Lot, Disposal, TaxRun, AuditLogEntry) mit EF-Core-Konfiguration, Beinen-Invarianten und Idempotenz-Schlüssel.
- Unit-Tests für Beinen-Invarianten und Idempotenz; `docs/03-datenmodell.md` mit ER-Diagramm; ADR 0002.
- CI und Sicherheit (P3): GitHub Actions `build-and-test` (Backend inkl. Testcontainers, Frontend Lint/Test/Build,
  Coverage-Artefakte, Mindestabdeckung TaxEngine 95 %, gitleaks), `codeql` (C# und TypeScript), Dependabot
  (NuGet, npm, Docker, Actions), Pull-Request-Vorlage, gitleaks-Pre-Commit-Hook (`.githooks/`).
- Börsen-API-Keys werden über `ISecretProtector` (ASP.NET Data Protection) verschlüsselt; Schlüsselring im Volume
  `dpkeys` außerhalb der Datenbank. User Secrets für lokale Entwicklung.
- ESLint (angular-eslint) im Frontend, `package-lock.json` eingecheckt, `docs/07-betrieb.md`.
