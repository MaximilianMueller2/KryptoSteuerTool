# Bitget-Fixtures

**Synthetisch, nicht aufgezeichnet.** Diese Bündel sind von Hand aus dem dokumentierten Antwortschema der Bitget-API
(V2 klassisch, V3 UTA; Quelle: `bitget-api` 3.2.6, Stand 20.10.2026) abgeleitet. Sie enthalten keine echten Daten
und keine Keys. Sobald echte, anonymisierte Antworten vorliegen, gehören sie hier hinein und ersetzen diese Dateien.

## Bitget-CSV (Futures)

- `futures-usdtm-start.csv`: **echte** Zeilen vom Anfang eines USDT-M-Futures-Exports (100 Zeilen, Kontostand beginnt bei 0), Order-Nummern durch
  Platzhalter ersetzt. Beträge, Zeitpunkte und Symbole sind unverändert. Format: BOM, CRLF, neueste Zeile zuerst, Tabulator vor der Order-Nummer, Zeitzone UTC+2.
- `futures-usdtm-mixed.csv`: synthetisch, deckt `adjust_*`, `user_grants_issue`, unbekannten Typ, Liquidation und Funding ab; Kontostand lückenlos.
- `futures-usdtm-gap.csv`: wie `mixed`, aber eine Zeile entfernt (Kontostand-Lücke).
