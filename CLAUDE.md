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
- P3 (Qualität, CI, Sicherheit) ist umgesetzt; siehe `docs/07-betrieb.md`. Backend und Frontend laufen in der CI.
- P4 (Import-Framework) ist umgesetzt; siehe `docs/04-importe.md`.
- P5 (Bitget-API) ist umgesetzt; siehe `docs/04-importe.md`. Die Bitget-Doku war nicht erreichbar, Endpunkte stammen aus dem npm-Paket `bitget-api` 3.2.6; Annahmen sind dort aufgeführt. Testdaten sind synthetisch; der Sync hat eine Oberfläche (Seite Konten).
- P6 (Bitget-CSV) ist für den Futures-Export umgesetzt und mit einer echten Datei geprüft (5.009 Zeilen); siehe `docs/04-importe.md`. Spot-, Ein-/Auszahlungs- und Earn-Exporte fehlen mangels Beispieldateien.
- P7 (Trade Republic CSV) ist umgesetzt und mit einer echten Datei geprüft (753 Zeilen); siehe `docs/04-importe.md`. Wertpapiere und Kapitalmaßnahmen werden gespeichert bzw. zur Prüfung vorgemerkt, die Auswertung folgt in P20. Das PDF-Fallback fehlt mangels Beispiel-PDFs.
- Steuer-Engine (P12 bis P19, Kern) ist als reine Logik umgesetzt und über 22 Golden-Test-Szenarien, Property-Tests und Randfälle abgesichert (Abdeckung 99 %); siehe `docs/05-steuerregeln.md`. Wertpapiere (P20) werden von der Krypto-Engine ignoriert; Kurse kommen bisher nur als EZB-Referenzkurs (USD-Stablecoins) und Handkurs (Kursdienst P21 offen).
- P12–P19 (Steuer-Engine) sind umgesetzt; siehe `docs/05-steuerregeln.md`. Berechnungslauf, Einzelaufstellung und EZB-Kurse sind über die API erreichbar (`/api/tax`, `/api/prices`); die Oberfläche (Angular) deckt Steuerjahr, Import, Prüfung und Kurse ab (P22, erste Stufe, mit Unit-Tests und einmal gegen die echte API angesehen). Die Seite „Konten“ hat den Bitget-Sync (Key hinterlegen, synchronisieren) und die Bestandsübersicht; der Sync wurde nie gegen die echte Bitget-API ausgeführt. Handkurse lassen sich auf der Seite Kurse eintragen. Die Review-Queue (`/api/review`) kennt zwei Entscheidungen: Transfer auf ein eigenes Konto und Prüfbuchung bestätigen.
- P25 (Steuerreport als PDF, Excel und CSV) ist umgesetzt; siehe `docs/06-berichte.md`. Offen: ELSTER-Ausfüllhilfe (P26), Wertpapiere (P20), Verfahrensdokumentation (P27).
- Bedienung: `docs/01-anleitung.md`; Betrieb, Backup und Restore: `docs/07-betrieb.md` und `scripts/`.
- Geprüft (20.10.2026): `dotnet build` ohne Warnungen, `dotnet test` (Domain, Importer und Integrationstests inkl. Testcontainers) grün, Frontend Lint/Test/Build grün, API gegen PostgreSQL gestartet (Migration, `/api/health`). Backend-Image gebaut und gestartet (Migration, Health, PDF-Erzeugung, Schlüsselring als unprivilegierter Benutzer; Build nur mit zusätzlichem Proxy-Zertifikat der Sandbox). Nicht geprüft: das Frontend-Image (Docker Hub in der Entwicklungsumgebung gedrosselt); vor dem Merge einmal `docker compose up --build` lokal ausführen. Backup- und Restore-Skript wurden gegen PostgreSQL getestet.
