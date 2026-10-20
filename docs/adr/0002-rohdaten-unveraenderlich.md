# ADR 0002: Rohdaten unveränderlich, Korrekturen als Overrides

- Status: Akzeptiert
- Datum: 2026-10-20

## Kontext

Jede Zahl im Steuerreport muss auf eine Rohzeile zurückführbar sein. Importformate ändern sich,
Börsen liefern Fehler, und der Nutzer muss Daten korrigieren können, ohne die Belege zu verlieren.

## Entscheidung

- `RawRecord` wird nur eingefügt, nie geändert oder gelöscht (Guard in `AppDbContext`).
- Normalisierte `Transaction`s verweisen auf ihre `RawRecord`s.
- Manuelle Korrekturen sind eigene Overrides; jede Änderung landet mit Vorher/Nachher und Begründung im `AuditLogEntry`.
- Importe sind idempotent über `(Quelle, externe ID)`, ersatzweise den Inhalts-Hash.

## Konsequenzen

- Reproduzierbarkeit: derselbe Rohbestand plus dieselben Overrides ergibt dasselbe Ergebnis.
- Mehr Speicher und etwas mehr Modellaufwand; dafür kein stiller Datenverlust.
- Fehlerhafte Rohdaten werden nicht repariert, sondern überlagert.

## Betrachtete Alternativen

- **Rohdaten direkt korrigieren:** einfacher, aber Belege gehen verloren und Läufe sind nicht reproduzierbar.
- **Event Sourcing für alles:** mächtig, für eine Person überdimensioniert.
