# 01 – Anleitung: von den Rohdaten zum Steuerreport

Diese Anleitung führt durch den normalen Ablauf. Die App ist eine **Berechnungshilfe, keine Steuerberatung**; prüfe die Ergebnisse vor der Abgabe.

## 1. Starten

```bash
cp .env.example .env     # optional: eigene Passwörter für die Datenbank setzen
docker compose up --build
```

Oberfläche: <http://localhost:4200>. Alle Ports sind nur auf deinem Rechner erreichbar (`127.0.0.1`). Es gibt **keine Anmeldung**: Stelle die App nicht ohne
Zugriffsschutz (VPN, Reverse-Proxy mit Login) ins Netz. Beim ersten Start legt die API die Datenbank selbst an.

## 2. Konten anlegen und Daten importieren

1. Seite **Import**: Ohne Konto bietet die Seite „Trade Republic“ und „Bitget“ als Vorlage an.
2. Datei wählen, Dateityp automatisch erkennen lassen, **Vorschau erzeugen**. Die Vorschau zeigt Zeilen, neue Buchungen, Duplikate, Fehler und Hinweise. Noch ist nichts gespeichert.
3. **Festschreiben** speichert Rohdaten (mit Prüfsumme) und Buchungen. **Verwerfen** löscht die Vorschau. Dieselbe Datei kann gefahrlos erneut importiert werden (Duplikate werden erkannt).

| Quelle | Wo bekomme ich die Datei? |
|---|---|
| Trade Republic | Transaktionsexport als CSV aus der Trade-Republic-App bzw. dem Webzugang (Menüpunkt kann sich ändern) |
| Bitget Futures | Bitget: Transaktionsverlauf der USDT-M-Futures als CSV exportieren. Ist der Zeitraum je Export begrenzt, mehrere Dateien nacheinander importieren (Überschneidungen sind unkritisch) |
| Bitget API | Seite **Konten**: API-Key mit **nur Leserechten** anlegen und eintragen, dann „Jetzt synchronisieren“ |

Genaue Spalten und Annahmen: `docs/04-importe.md`. Bitget Spot-, Einzahlungs- und Earn-Exporte werden noch nicht als CSV unterstützt; die API kann sie abrufen.

## 3. Kurse für Euro-Werte

Bitget-Futures laufen in USDT. Für die Umrechnung braucht die App EUR-Kurse: Seite **Kurse** → **Online abrufen** (EZB) oder die Datei `eurofxref-hist.zip` von der EZB hochladen.
Fehlende Kurse werden **nie als 0 € angenommen**, sondern als Befund gemeldet. Einzelne Kurse trägst du auf derselben Seite von Hand ein (mit Quelle).

## 4. Prüfen

Seite **Prüfung** listet offene Befunde. Typische Fälle:

- **Auszahlung ohne Gegenstück:** ging das Geld auf ein eigenes Konto (z. B. Bitget Wallet), „Als eigenen Transfer markieren“. Das ist steuerneutral, Kaufdatum und Kosten wandern mit.
- **Buchung zur Prüfung** (z. B. Wertpapier-Kapitalmaßnahmen bei Trade Republic): ansehen und „Geprüft“ bestätigen. Wertpapiere wertet die App noch nicht aus.
- **Kurs fehlt / Bestand reicht nicht:** Kurs nachtragen bzw. fehlende Importe (z. B. früherer Zeitraum) nachholen.

Jede Entscheidung steht im Audit-Log und erscheint im Bericht; sie lässt sich zurücknehmen. Rohdaten werden nie verändert.

## 5. Berechnen und Bericht erzeugen

Seite **Steuerjahr**: Jahr wählen, optional Verlustvortrag und andere private Veräußerungsgeschäfte eintragen, **Berechnen**. Solange Befunde offen sind, gilt das Ergebnis als **Entwurf**.
Danach PDF, Excel oder CSV herunterladen (Details: `docs/06-berichte.md`). Den Bericht bewahrst du als Beleg auf.

## 6. Sichern

```bash
scripts/backup.sh            # Datenbank und Schlüsselring nach ./backups
scripts/restore.sh backups/db-….dump [backups/keys-….tar.gz]
```

Mache vor jedem größeren Import und nach der Steuererklärung ein Backup und lege es verschlüsselt und getrennt vom Rechner ab.

## 7. Wohin mit den Zahlen? (Steuererklärung, z. B. mit WISO)

Die folgenden Hinweise sind allgemeine Orientierung aus öffentlichen Quellen, **keine Beratung**; Formulare und Programmmenüs ändern sich von Jahr zu Jahr. Prüfe die Zeilen im aktuellen Formular.

- **Verkäufe und Tausch von Krypto (§ 23 EStG):** Anlage SO, Abschnitt „Private Veräußerungsgeschäfte“, andere Wirtschaftsgüter (Kryptowerte). Dort kommen Zeitraum, Veräußerungspreis, Anschaffungskosten,
  Werbungskosten (Gebühren) und Gewinn/Verlust hin: genau die Zeilen im Abschnitt „Anlage SO“ der Zusammenfassung im Bericht. Bei vielen Einzelgeschäften reicht in der Regel eine Summe mit Verweis auf die beigefügte Einzelaufstellung.
  Gewinne unter 1.000 € im Jahr (bis 2023: 600 €) sind steuerfrei (Freigrenze); wird sie erreicht, ist der **gesamte** Gewinn steuerpflichtig. Verkäufe nach mehr als einem Jahr Haltedauer sind steuerfrei.
- **Staking, Lending und ähnliche Einnahmen (§ 22 Nr. 3 EStG):** ebenfalls Anlage SO, eigener Abschnitt „Einkünfte aus sonstigen Leistungen“; Freigrenze 256 €.
- **Futures (Bitget):** gelten als Termingeschäfte, also Einkünfte aus Kapitalvermögen: Anlage KAP (ausländischer Anbieter ohne inländischen Steuerabzug). Die frühere 20.000-€-Grenze für Terminverluste ist
  durch das Jahressteuergesetz 2024 rückwirkend ab 2024 entfallen. Weil du hauptsächlich Futures handelst, lohnt sich hier eine kurze Rücksprache mit Steuerberatung oder Finanzamt, vor allem zur Einordnung und zu Verlusten.
- **Trade Republic (Wertpapiere):** Die Jahressteuerbescheinigung des Brokers ist maßgeblich; Krypto bei Trade Republic ist dort nicht enthalten und gehört in die Anlage SO.
- **Belege:** Einzelaufstellung (PDF/Excel) bereithalten oder beifügen. Eine automatische Übermittlung an ELSTER gibt es nicht.

Quellen für Zeilenangaben und Hintergründe (Stand Recherche 20.10.2026, bitte aktuell gegenprüfen): [Blockpit: Krypto in WISO eintragen](https://www.blockpit.io/de-de/steuer-guides/krypto-wiso-steuer-eintragen-anleitung),
[Waltio: Krypto-Steuererklärung 2026](https://www.waltio.com/de/krypto-steuererklaerung-2026/),
[CMS: Verlustverrechnung bei Termingeschäften entfällt](https://cms.law/de/deu/legal-updates/ade-verlustabzugsbeschraenkung-fuer-termingeschaefte-gesetzgeber-bereinigt-verfassungswidrigkeit).

## Wenn etwas nicht stimmt

| Beobachtung | Mögliche Ursache |
|---|---|
| „API nicht erreichbar“ | `docker compose ps`: läuft `api` und ist `healthy`? Logs: `docker compose logs api` |
| Datei wird nicht erkannt | Dateityp von Hand wählen; Datei unverändert exportieren (kein Excel-Speichern) |
| Bitget-Zeitzone falsch | Beim Import „Zeitzone der Datei“ angeben (z. B. `UTC+2`); die App leitet sie sonst aus den Funding-Zeiten ab |
| Bestand negativ / „Bestand reicht nicht“ | Es fehlt Historie (früherer Export, Transfer von anderswo) |
| Futures-Summe 0 € | EUR-Kurse fehlen: Seite Kurse |
