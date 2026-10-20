# Negativer Bestand → Review statt Ergebnis

Steuerjahr der Auswertung: 2025. Eingabe und erwartetes Ergebnis stehen in `15-negativer-bestand-review.json`.

## Hand-Rechnung

Verkauf von 1 BTC am 01.05.2025, aber es gibt keinen Kauf (Historie fehlt). Anschließend ein Kauf am 01.06.2025.

- Die Engine bucht **keinen** Verkauf mit 0 EUR Kosten, sondern erzeugt den Befund *NegativeHolding* und **stoppt** die Berechnung für BTC.
- Der spätere Kauf wird übersprungen; dazu gibt es den Befund *AssetBlocked*. Ergebnis § 23: keine Veräußerungen.
