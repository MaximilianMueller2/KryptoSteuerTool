# ADR 0001: Modularer Monolith statt Microservices

- Status: Akzeptiert
- Datum: 2026-10-20

## Kontext

Die App wird von einer Person betrieben und genutzt. Sie besteht aus Importen, einem Ledger, einer
Steuer-Engine, Reports und einer Oberfläche. Die Steuerlogik muss exakt, reproduzierbar und
unabhängig von Datenbank und Netzwerk testbar sein. Betrieb und Wartung sollen so einfach sein, dass
ein einzelner Server mit Docker Compose reicht.

## Entscheidung

Wir bauen einen **modularen Monolithen**:

- Ein Backend-Prozess (ASP.NET Core) mit klar getrennten Projekten: `Domain`, `Application`,
  `Infrastructure`, `Importers`, `TaxEngine`, `Api`.
- Eine Angular-Oberfläche, die nur über die Web-API auf das Backend zugreift.
- Eine PostgreSQL-Datenbank.
- `TaxEngine` hängt nur von `Domain` ab und kennt keine Datenbank: reine Funktionen, Golden Tests.
- Die Abhängigkeitsregeln werden durch Architekturtests abgesichert.

## Konsequenzen

- Ein Deployment, ein Backup, eine Datenbank-Transaktionsgrenze; kein verteiltes Debugging.
- Die Grenzen zwischen den Modulen bleiben über Projektverweise erzwungen, nicht nur durch Disziplin.
- Skalierung über mehrere Instanzen ist nicht Ziel. Für eine Person ist das unkritisch.
- Sollte später ein Modul (z. B. Importe) ausgelagert werden müssen, ist die Naht bereits vorhanden.

## Betrachtete Alternativen

- **Microservices:** unnötiger Betriebsaufwand (Netzwerk, Versionierung, verteilte Konsistenz) für einen
  Einzelnutzer; erschwert reproduzierbare Steuerläufe.
- **Ein einzelnes Projekt ohne Schichten:** schneller gestartet, aber die Steuerlogik wäre nicht
  isoliert testbar und Abhängigkeiten würden unkontrolliert wachsen.
