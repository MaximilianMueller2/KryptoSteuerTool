# Trade-Republic-Fixtures

- `traderepublic-sample.csv`: **echte** Zeilen aus einem Transaktionsexport (je Buchungsart bis zu 3, bei Zinsen, Steueroptimierung und
  Kapitalmaßnahmen bis zu 5 Zeilen). Beträge, Zeitpunkte, Stückzahlen, ISINs und Namen der Wertpapiere sind unverändert. Entfernt oder
  ersetzt wurden: eigener Name, IBAN, Gegenpartei, Verwendungszweck, Händlernamen der Kartenzahlungen, Beschreibungen von Überweisungen;
  die `transaction_id`-Werte sind neu erzeugt.
- `traderepublic-edge-cases.csv`: synthetisch (fehlerhafte Zeilen, unbekannte Typen, andere Währung, Steuer auf Verkauf, Pseudo-ISIN).
