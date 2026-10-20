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
- EF-Core-Migration `Initial`, automatische Migration beim Start (`Database:MigrateOnStartup`), Persistenz-Integrationstests.
- Import-Framework (P4): `IImporter`, Importablauf (Rohdatei mit SHA-256, Parsen, Normalisieren, Duplikaterkennung,
  Vorschau, Bestätigung), Importbericht mit Zeilennummern, `NeedsReview` für unbekannte Typen.
- API: `POST /api/imports`, `GET /api/imports/{id}`, `POST /api/imports/{id}/commit`, `DELETE /api/imports/{id}`,
  dazu `GET/POST /api/accounts`.
- Importer `generic-csv` als Referenz, Beispieldateien unter `tests/fixtures/`, `docs/04-importe.md`.
- Migration `ImportRuns` (Tabelle `ImportRuns`, `RawRecord.ContentHash`/`LineNumber`); neuer Transaktionstyp `Unknown`.
- Bitget-API-Anbindung (P5): signierter, nur lesender HTTP-Client mit Zeitabgleich und Retry/Backoff, Kontotyp-Erkennung
  (klassisch/UTA), Rechteprüfung mit Warnung, Abruf von Spot-Fills, Ein-/Auszahlungen, Bills, Futures, Convert und Earn in
  30-Tage-Fenstern (höchstens 90 Tage) mit Cursor-Pagination, Importer `bitget-api`, inkrementeller Sync je Endpunkt,
  täglicher Hintergrundjob, Saldo-Abgleich mit Review-Queue.
- API: `PUT/GET/DELETE /api/accounts/{id}/bitget`, `POST /api/accounts/{id}/bitget/sync`, `GET /api/review-items`.
- Entitäten `ExchangeCredential`, `SyncState`, `ReviewItem`; Migration `BitgetSync`; Doku mit Anleitung „Bitget-API-Key mit Leserechten anlegen“.
- Bitget-CSV-Import für Futures (P6): Format-Erkennung an der Kopfzeile, Zeitzonen-Erkennung aus den Funding-Zeilen (Option `timeZone`),
  Kontostand-Prüfung über die Spalte `Wallet balance`, Zuordnung aller Buchungsarten der Beispieldatei (Funding, Gebühren, Gewinn/Verlust,
  Liquidation, Bonus, interne Umbuchungen); gemeinsame Zuordnung mit dem API-Importer.
- Import-Framework: Importoptionen (`ImportInput.Options`, gespeichert am Lauf), Hinweise im Importbericht (`warnings`) und Abgleich gegen
  Buchungen anderer Quellen, damit dieselbe Buchung aus CSV und API nicht doppelt gezählt wird. Migration `ImportOptions`.
- Trade-Republic-CSV-Import (P7): Format-Erkennung an der Kopfzeile, Krypto (Kauf, Verkauf, Lieferung), Wertpapiere (Aktien, Fonds, Derivate),
  Zinsen, Dividenden, Fondserträge, Steuern als eigene Buchungen, Saveback/Stockperk, Geschenke, Gebühren, Ein-/Auszahlungen; Kartenzahlungen als
  `Ignored`, Kapitalmaßnahmen und Tilgungen in der Prüfliste. Neuer Buchungstyp `Tax`, Asset-Klassen und ISIN für Wertpapiere.
- Gemeinsamer `ImportTxBuilder` für alle Importer; mehrere Buchungen je Zeile über Suffixe der externen ID.

### Behoben

- XML-Kommentar in `KryptoSteuer.Api.csproj` (`--` ist in XML-Kommentaren unzulässig) ließ das Projekt nicht laden.
- Veralteter `PostgreSqlBuilder()`-Konstruktor (Testcontainers) ersetzt.
- Uneinheitliche EF-Core-Versionen (10.0.4 gegen 10.0.13) zwischen den Projekten.
- Assembly-Anker mit `[ExcludeFromCodeCoverage]`, damit die TaxEngine-Abdeckungsprüfung nicht an ihnen scheitert.
- Docker-Volume `/keys` im API-Image für den unprivilegierten Benutzer beschreibbar.
