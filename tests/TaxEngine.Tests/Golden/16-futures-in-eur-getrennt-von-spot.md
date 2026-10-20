# Futures: Gewinn, Funding, Liquidation und Gebühren in EUR

Steuerjahr der Auswertung: 2025. Eingabe und erwartetes Ergebnis stehen in `16-futures-in-eur-getrennt-von-spot.json`.

## Hand-Rechnung

Bitget-Futures am 02.03.2025, USDT-Kurs 0,90 EUR (Tageskurs vom 01.03.2025).

| Buchung | USDT | EUR |
|---|---|---|
| Realisierter Gewinn | +100, Gebühr 2 | +90, Gebühr 1,80 |
| Funding | −5 | −4,50 |
| Liquidation | −50, Gebühr 1 | −45, Gebühr 0,90 |
| Gebühr | −0,5 | −0,45 |

- Realisierte Gewinne 90; Verluste 0; Funding −4,50; Liquidation −45; Gebühren 1,80 + 0,90 + 0,45 = **3,15**.
- Netto = 90 − 4,50 − 45 − 3,15 = **37,35 EUR** (Termingeschäfte, Anlage KAP; keine Haltefrist, nie mit Spot-Lots vermischt).
