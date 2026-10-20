# CLAUDE.md – Regeln für den Coding-Agenten

## Projektziel

Selbst gehostete Alternative zu CoinTracking für eine einzelne Person in Deutschland.
Sie importiert alle Transaktionen (Bitget per API und CSV, Trade Republic per CSV, weitere
Broker per CSV), berechnet Gewinne, Haltefristen und Einkünfte nach deutschem Steuerrecht und
erzeugt am Jahresende einen vollständig dokumentierten Steuerreport. Fokus: Krypto; Aktien und
ETFs folgen später. Die App ersetzt keine Steuerberatung.

## Arbeitsweise pro Aufgabe

1. Zuerst `CLAUDE.md` und `docs/` lesen.
2. Tests zuerst schreiben, dann implementieren.
3. `docs/` und `CHANGELOG.md` aktualisieren.
4. Pull Request öffnen; mergen erst, wenn alle Tests grün sind.

## Tech-Stack

- Backend: .NET 10 (aktuelle LTS), ASP.NET Core Web API, EF Core, PostgreSQL
- Frontend: Angular 22 (Standalone Components, Signals, Angular Material, i18n Deutsch)
- Betrieb: Docker Compose
- Steuerlogik: eigenes Projekt `TaxEngine`, hängt nur von `Domain` ab, keine Datenbank, reine Funktionen

## Coding-Regeln

- Tests zuerst.
- `decimal` statt `double` für Geld und Mengen. Rundung erst in der Ausgabe (Report).
- Alle Zeitstempel in UTC speichern. Steuerliche Tagesgrenzen werden in `Europe/Berlin` berechnet.
- Rohdaten werden nie verändert oder gelöscht; Korrekturen sind eigene Overrides mit Audit-Log.
- Fehlende Daten (Kurs, Kostenbasis) werden nie stillschweigend als 0 angenommen, sondern landen in der Review-Queue.
- Abhängigkeitsrichtung: `Domain` ← `Application` ← `Infrastructure`/`Importers`/`Api`; `TaxEngine` ← nur `Domain`.
- Keine Geheimnisse (API-Keys, Passwörter) in Code, Commits, Issues oder Logs. Lokal: .NET User Secrets; in der Datenbank nur verschlüsselt über `ISecretProtector`.

## Steuerregeln

- § 23 EStG: Veräußerung steuerfrei, wenn zwischen Anschaffung und Veräußerung mehr als ein Jahr liegt. Fristbeginn = Vertragsabschluss.
- Tausch Krypto->Krypto, Krypto->Stablecoin, Bezahlen mit Krypto, Wrapping = Veräußerung + Neuanschaffung.
- Transfers zwischen eigenen Konten sind steuerneutral, Anschaffungsdaten wandern mit.
- Verbrauchsfolge FIFO je Konto (Wallet/Börse) als Standard, Methode stetig.
- Freigrenze § 23: Jahresgewinn < 1.000 EUR steuerfrei, sonst alles steuerpflichtig.
- § 22 Nr. 3 (Staking, Lending, Airdrops mit Gegenleistung): Marktwert bei Zufluss, Freigrenze < 256 EUR; Zufluss = Anschaffung.
- Reform-Entwurf (nicht beschlossen, Stand 08.09.2026): für Tauschkryptowerte, angeschafft oder zugeflossen nach 31.12.2026, keine Haltefrist mehr, 25 % Abgeltungsteuer + Soli, Sparer-Pauschbetrag; Altbestand bis 31.12.2026 bleibt im alten Recht. Muss als abschaltbarer Regelsatz umgesetzt werden, Standard AUS bis zur Verkündung im BGBl.

## Stand der Umsetzung

- P1 (Projektgerüst) ist umgesetzt; siehe `docs/02-architektur.md`.
- P2 (Datenmodell) ist als Code und Doku umgesetzt; siehe `docs/03-datenmodell.md`. Die EF-Migration `Initial` liegt unter `src/backend/Infrastructure/Persistence/Migrations`.
- P3 (Qualität, CI, Sicherheit) ist umgesetzt; siehe `docs/07-betrieb.md`. Frontend (Lint, Test, Build) wurde lokal geprüft, das Backend noch nicht.
- P4 (Import-Framework) ist umgesetzt; siehe `docs/04-importe.md`. Konkrete Importer (Bitget, Trade Republic) folgen.
- P5 (Bitget-API) ist umgesetzt; siehe `docs/04-importe.md`. Die Bitget-Doku war nicht erreichbar, Endpunkte stammen aus dem npm-Paket `bitget-api` 3.2.6; Annahmen sind dort aufgeführt. Testdaten sind synthetisch. Es gibt noch keine Oberfläche für den Sync.
- P6 (Bitget-CSV) ist für den Futures-Export umgesetzt und mit einer echten Datei geprüft (5.009 Zeilen); siehe `docs/04-importe.md`. Spot-, Ein-/Auszahlungs- und Earn-Exporte fehlen mangels Beispieldateien.
- P7 (Trade Republic CSV) ist umgesetzt und mit einer echten Datei geprüft (753 Zeilen); siehe `docs/04-importe.md`. Wertpapiere und Kapitalmaßnahmen werden gespeichert bzw. zur Prüfung vorgemerkt, die Auswertung folgt in P20. Das PDF-Fallback fehlt mangels Beispiel-PDFs.
- Steuer-Engine (P12 bis P19, Kern) ist als reine Logik umgesetzt und über 22 Golden-Test-Szenarien, Property-Tests und Randfälle abgesichert (Abdeckung 99 %); siehe `docs/05-steuerregeln.md`. Noch nicht angeschlossen: Kursdienst (P21), Berechnungslauf über die API, Report (P25). Wertpapiere (P20) werden von der Krypto-Engine ignoriert.
- Geprüft (20.10.2026): `dotnet build` ohne Warnungen, `dotnet test` (Domain, Importer und Integrationstests inkl. Testcontainers) grün, Frontend Lint/Test/Build grün, API gegen PostgreSQL gestartet (Migration, `/api/health`). Nicht geprüft: Docker-Image-Build und das Frontend-Image (in der Entwicklungsumgebung ohne Zugriff auf Docker Hub bzw. NuGet im Container); vor dem Merge einmal `docker compose up --build` lokal ausführen.
