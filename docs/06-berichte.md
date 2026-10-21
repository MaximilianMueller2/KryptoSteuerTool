# Berichte (P25)

Der Jahresbericht liegt in drei Formaten vor. Alle drei werden aus denselben Daten (`TaxReportData`) erzeugt, die `ReportService` zusammenstellt;
die Renderer im Projekt `KryptoSteuer.Reports` formatieren nur.

| Format | Endpunkt | Inhalt |
|---|---|---|
| PDF (A4 quer, QuestPDF) | `GET /api/reports/{jahr}/pdf?name=&reform=&method=` | alle Abschnitte |
| Excel (ClosedXML) | `GET /api/reports/{jahr}/xlsx` | ein Blatt je Abschnitt, Beträge als Zahlen |
| CSV | `GET /api/reports/{jahr}/csv` | Einzelaufstellung, Semikolon, deutsches Zahlenformat, UTF-8 mit BOM |

`name` erscheint auf dem Deckblatt (optional, wird nicht gespeichert). Die Oberfläche (Seite Steuerjahr) bietet die Downloads nach einer Berechnung an.

## Abschnitte

1. **Deckblatt:** Name, Steuerjahr, Erstellungsdatum, Regelsatz-Version, Verbrauchsfolge, Kursquellen, Status Entwurf oder final, Hash der Eingabedaten.
   Offene Befunde machen den Bericht zum **Entwurf**; erst ohne offene Befunde steht dort „final“.
2. **Zusammenfassung zum Eintragen:** Anlage SO (§ 23 und § 22 Nr. 3) mit Hinweis zur Freigrenze, Anlage KAP (Futures) und ein Hinweis zu Wertpapieren.
3. **Einzelaufstellung** aller Veräußerungen des Jahres.
4. **Laufende Einkünfte** je Zufluss mit Kurs und Quelle.
5. **Bestand zum 31.12.** je Konto mit Lots, Restkosten, Regime Alt/Neu und „steuerfrei ab“.
6. **Manuelle Korrekturen** aus dem Audit-Log (Entscheidungen der Review-Queue) mit Begründung.
7. **Quellen:** festgeschriebene Importdateien mit SHA-256 und Zeitraum der Buchungen.

## Regeln

- **Berechnung bis Jahresende:** Der Bericht rechnet nur mit Buchungen bis zum 31.12. (Europe/Berlin) des Jahres. Dadurch entspricht der Bestand dem 31.12., und
  Befunde aus späteren Jahren machen den Bericht nicht zum Entwurf. Die Eingabe-Hash im Bericht bezieht sich auf diese Teilmenge und weicht deshalb vom Hash eines
  Berechnungslaufs (`/api/tax/runs`) ab.
- **Anlage SO § 23:** Veräußerungspreis, Anschaffungskosten, Gebühren und Gewinn sind die Summen der **steuerpflichtigen** Zeilen der Einzelaufstellung (gerundet);
  steuerfreie Veräußerungen nach Ablauf der Frist erscheinen nur in der Einzelaufstellung.
- **Rundung:** kaufmännisch auf den Cent, erst in der Ausgabe. Die Summen sind die Summen der gerundeten Zeilen und stimmen daher auf den Cent mit der
  Einzelaufstellung überein (Integrationstest vergleicht Excel, CSV und API).
- **Lizenz:** QuestPDF wird mit der Community-Lizenz genutzt (kostenlos für Privatpersonen und kleine Unternehmen).
- Die App ersetzt keine Steuerberatung; jede Seite des PDFs trägt diesen Hinweis.

## Noch offen

- Wertpapiere (P20): nur ein Hinweistext, keine Auswertung. Reform-Abschnitt (Neu-Krypto) fehlt im Bericht, solange der Regelsatz aus ist.
- ELSTER-Ausfüllhilfe mit Zeilennummern je Vordruck und Kopier-Knopf (P26) folgt; die Zeilen müssen vorher mit dem amtlichen Vordruck abgeglichen werden.
