# Teilverkauf über drei Lots (FIFO)

Steuerjahr der Auswertung: 2025. Eingabe und erwartetes Ergebnis stehen in `04-teilverkauf-ueber-drei-lots-fifo.json`.

## Hand-Rechnung

Käufe: Lot 1 = 1 BTC für 10.000 (01.01.), Lot 2 = 1 BTC für 20.000 (01.02.), Lot 3 = 1 BTC für 30.000 (01.03.2025).
Verkauf 2,5 BTC am 01.04.2025 für 62.500 EUR (= 25.000 je BTC).

FIFO verbraucht Lot 1 ganz, Lot 2 ganz, Lot 3 zur Hälfte; der Erlös wird nach Menge verteilt:

| Lot | Menge | Erlös | Kosten | Gewinn |
|---|---|---|---|---|
| 1 | 1,0 | 25.000 | 10.000 | 15.000 |
| 2 | 1,0 | 25.000 | 20.000 | 5.000 |
| 3 | 0,5 | 12.500 | 15.000 | −2.500 |

- Gewinne 20.000, Verluste 2.500, Netto **17.500 EUR** (steuerpflichtig).
- Restbestand: 0,5 BTC aus Lot 3 mit Restkosten 15.000 EUR.
