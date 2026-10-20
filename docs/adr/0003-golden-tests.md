# ADR 0003: Golden Tests mit Hand-Rechnung für die Steuer-Engine

- Status: Akzeptiert
- Datum: 2026-10-20

## Kontext

Die Steuer-Engine rechnet Geldbeträge, deren Fehler erst bei der Steuererklärung auffallen. Ein Test, der die erwartete Zahl aus derselben
Logik berechnet wie der Code, fängt Denkfehler nicht ab.

## Entscheidung

- Jedes Szenario besteht aus Eingabebuchungen und dem **von Hand gerechneten** erwarteten Ergebnis (JSON) sowie der Rechnung als Markdown daneben.
- Die erwarteten Zahlen werden nie vom Code erzeugt.
- Die Golden Tests sind Pflicht in der CI. Ändert sich ein erwartetes Ergebnis, braucht die Änderung einen ADR, der Regel, Rechengrund und
  Quelle nennt.
- Ergänzt werden sie durch Property-Tests (Invarianten wie Mengen- und Kostenerhaltung) und Randfall-Tests.

## Konsequenzen

- Regeländerungen (Gesetz, Reform-Stichtag) werden sichtbar und nachvollziehbar.
- Mehr Aufwand beim Ändern der Engine; dafür gibt es keine stillen Ergebnisverschiebungen.

## Betrachtete Alternativen

- **Nur Unit-Tests mit berechneten Erwartungen:** schneller geschrieben, aber nicht unabhängig vom Code.
- **Vergleich mit einer anderen Software:** nützlich als Stichprobe, aber keine Wahrheit; Abweichungen wären erst zu klären.
