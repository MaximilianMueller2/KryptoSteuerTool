# Transfer ohne Gegenstück und Tausch ohne Kurs

Steuerjahr der Auswertung: 2025. Eingabe und erwartetes Ergebnis stehen in `18-unbekannter-transfer-und-fehlender-kurs.json`.

## Hand-Rechnung

Kauf 1 BTC (Konto TR) am 01.02.2025 für 20.000 EUR. 01.03.2025: Auszahlung 0,5 BTC ohne passenden Eingang → Befund *UnmatchedTransferOut*, die Lots bleiben (Bestand 1 BTC).
01.04.2025: Tausch 0,2 BTC → 100 XYZ **ohne Kurs** für XYZ und BTC → Befund *MissingPrice*; beide Assets werden gestoppt (nie 0 EUR).
