# 05 – Steuerregeln

Stand: 20.10.2026. Die App ersetzt keine Steuerberatung. Dieses Kapitel beschreibt, **was die Steuer-Engine rechnet**, mit Rechtsgrundlage,
Beispiel und Testverweis. Alle Regeln liegen im Projekt `TaxEngine` (reine Logik, keine Datenbank). Die Rechtsgrundlagen sind Gesetzestexte
und das BMF-Schreiben vom 06.03.2025; ich konnte sie in der Entwicklungsumgebung nicht online abrufen, sie stehen so im Prompt-Katalog
und im Gesetzestext, wie ich ihn kenne. **Vor der Abgabe prüfen oder mit einer Beraterin klären.**

## Regelsätze (`Rules/RuleSetCatalog.cs`)

Jeder Parameter hat Gültigkeit (ab/bis Veranlagungszeitraum), Rechtsgrundlage, Quelle und steht als JSON im Snapshot des Berechnungslaufs.

| Regelsatz | Parameter | Wert | Gültig | Rechtsgrundlage |
|---|---|---|---|---|
| `DE-23EStG` 2026.1 (geltendes Recht) | Haltefrist | 1 Jahr | immer | § 23 Abs. 1 Satz 1 Nr. 2 EStG |
| | Freigrenze § 23 | 600 EUR | bis VZ 2023 | § 23 Abs. 3 Satz 5 EStG |
| | Freigrenze § 23 | 1.000 EUR | ab VZ 2024 | § 23 Abs. 3 Satz 5 EStG |
| | Freigrenze § 22 Nr. 3 | 256 EUR | immer | § 22 Nr. 3 Satz 2 EStG |
| `DE-Reform-2027-Entwurf` 2026-09-08 (Entwurf, **nicht beschlossen**) | Abgeltungsteuer | 25 % | ab 2027 | Referentenentwurf vom 08.09.2026 |
| | Solidaritätszuschlag | 5,5 % (26,375 % gesamt) | ab 2027 | § 4 SolzG |
| | Sparer-Pauschbetrag | 1.000 / 2.000 EUR | ab 2027 | § 20 Abs. 9 EStG |

**Reform-Schalter** („Reform 2027 anwenden“, Standard **aus**): Solange er aus ist, wird alles nach § 23 gerechnet. Jedes Lot trägt trotzdem schon sein
Regime (Alt/Neu), damit ein späteres Einschalten nur eine Neuberechnung ist. Ein Wechsel erzeugt eine Warnung im Report (geplant, P25).

## Haltefrist und Stichtage (`Time/HoldingPeriod.cs`, `Time/BerlinTime.cs`)

- Fristberechnung nach §§ 187, 188 BGB auf Kalendertagen **in Europe/Berlin**. Die Frist endet mit Ablauf des Jahrestags; steuerfrei ist ein Verkauf ab dem Tag danach.
  - Kauf 15.03.2025 → Frist endet 15.03.2026 → steuerfrei ab **16.03.2026**.
  - Kauf 29.02.2024 → Frist endet 28.02.2025 → steuerfrei ab **01.03.2025**.
- Fristbeginn ist der Vertragsabschluss (Zeitstempel des Trades), nicht die Gutschrift.
- Berliner Zeit ist eigenständig implementiert (EU-Sommerzeit-Regel), nicht von der Zeitzonendatenbank des Systems abhängig; der Test vergleicht sie stündlich von 2023 bis 2029 mit der Systemdatenbank.
- **Regime-Tag je Lot:** *Neu*, wenn die Anschaffung (Berliner Datum) am oder nach dem 01.01.2027 liegt **und** das Asset ein Tauschkryptowert ist (Standard: alle Krypto und Stablecoins; Ausnahmen konfigurierbar). Sonst *Alt*.
- Tests: `HoldingPeriodTests`, `BerlinTimeTests`; Golden 01, 02, 03, 12, 13.

## Lots und Verbrauchsfolge (`Engine/TaxCalculator.cs`)

- Jeder **Zugang** (Kauf, Tausch, Staking, Airdrop, Bonus, Transfer-Eingang) erzeugt ein Lot mit Anschaffungszeitpunkt und Kosten in EUR.
- Jeder **Abgang** verbraucht Lots **FIFO je Konto** (Wallet/Börse) und erzeugt je verbrauchtem Lot einen Disposal-Datensatz (Teilverbrauch möglich). Erlös, Kosten und Gebühren werden nach Menge verteilt.
- Alternative **Durchschnittsmethode** (gleitend): die Menge wird anteilig aus allen Lots entnommen, Kosten je Einheit = Durchschnitt. Die Methode ist hinter einer Schnittstelle austauschbar und muss für ein Jahr stetig bleiben.
- Reihenfolge von Alt- und Neu-Lots im selben Konto konfigurierbar (Standard FIFO, also Alt zuerst; Alternative „Neubestand zuerst“), weil der Entwurf das nicht abschließend regelt.
- **Negative Bestände** stoppen die Berechnung für dieses Asset und erzeugen einen Befund; es wird nie mit 0 EUR Kosten weitergerechnet. Spätere Buchungen des Assets werden übersprungen und gemeldet (*AssetBlocked*).
- **Deterministisch:** Sortierung nach Zeitstempel, dann externer ID, dann ID. Gleiche Eingabe ergibt gleiches Ergebnis, auch bei anderer Reihenfolge der Eingabe.
- Tests: Golden 04, 15, 17; Property-Tests (`LotInvariantProperties`): Summe der Lot-Restmengen = Bestand je Konto zu jedem Zeitpunkt, Kostenerhaltung, Mengen- und Erlösverteilung, Reihenfolge-Unabhängigkeit.

## Tausch, Stablecoins, Wrapping, Gebühren

- **Krypto → Krypto, Krypto → Stablecoin, Bezahlen mit Krypto:** Abgang zum EUR-Marktwert (Veräußerungspreis), Zugang des erhaltenen Assets zum selben Wert. Maßgeblich ist der Kurs des **Erhaltenen**; fehlt er, der Kurs des Hingegebenen (BMF-Schreiben vom 06.03.2025). Fehlt beides: Befund, nie 0 EUR. Tests: Golden 05; `Bezahlen_mit_Krypto_*`.
- **Wrapping/Unwrapping** (BTC↔WBTC, ETH↔WETH, Liquid-Staking-Token): Standard = Tausch mit neuer Frist (BMF-Schreiben vom 06.03.2025, Rn. 55). Test: Golden 11. *Konfigurierbar je Asset-Paar ist als Einstellung vorbereitet (`WrappingNotATaxEvent`), aber noch nicht in der Berechnung verdrahtet.*
- **Gebühren:** Kaufgebühren in EUR erhöhen die Anschaffungskosten; Verkaufsgebühren mindern den Erlös (Gebührenanteil je Disposal). Gebühr im erhaltenen Coin mindert die Lot-Menge; im hingegebenen Coin erhöht sie die verbrauchte Menge; in einem **dritten Coin** (z. B. BGB) ist sie ein eigener Abgang dieses Coins, dessen Marktwert die Kosten bzw. den Erlös beeinflusst. Tests: Golden 10, `Gebuehr_*`.
- **Dust, Convert und Kleinstbeträge** werden wie Tausch behandelt (Typ `Trade`).

## Transfers zwischen eigenen Konten (`Engine/TransferMatcher.cs`)

- Auszahlung (TransferOut/Withdrawal) und Eingang (TransferIn/Deposit) werden über Asset, Menge minus Netzwerkgebühr (Toleranz 2 %) und Zeitfenster (Standard 48 h) gepaart; jede Zuordnung hat eine Konfidenz, unter der Mindest-Konfidenz (Standard 0,6) wird nicht gepaart, sondern ein Vorschlag gemeldet. (Es gibt noch keine Transaktions-Hash-Zuordnung, weil die Importer den Hash nicht liefern.)
- Ein Transfer ist **steuerneutral**: die Lots wandern mit ursprünglichem Anschaffungsdatum und Kosten ins Zielkonto, das sie nach Anschaffungsdatum einreiht. Test: Golden 06.
- **Netzwerkgebühr** (Menge Auszahlung minus Eingang): Option (a, Standard) Abgang ohne Erlös, steuerlich neutral markiert und im Report gesondert ausgewiesen mit Hinweis „mit Berater klären“; Option (b) Abgang mit Erlös in Höhe des Marktwerts (Veräußerung). Tests: Golden 06, `Netzwerkgebuehr_als_Veraeusserung_*`.
- **Ohne Gegenstück:** Eine Auszahlung bleibt im Quellkonto (Befund *UnmatchedTransferOut*, Nutzer entscheidet: eigenes externes Konto, Verkauf außerhalb, Schenkung, Verlust). Ein Eingang erzeugt kein Lot, weil Datum und Kosten fehlen (*UnmatchedTransferIn*). Test: Golden 18.

## Laufende Einkünfte, § 22 Nr. 3 EStG

- Staking, Lending, Zinsen, Bonus/Referral und Airdrops **mit** Gegenleistung: Einnahme = EUR-Marktwert im Zuflusszeitpunkt; zugleich Anschaffung (Lot mit diesen Kosten, Frist beginnt mit dem Zufluss).
- Airdrop **ohne** Gegenleistung (Standard): Einnahme 0, Kosten 0, Frist beginnt mit dem Zufluss; je Airdrop über die Einstellung klassifizierbar.
- Jahressumme abzüglich Werbungskosten (Eingabe): Freigrenze **256 EUR** (unter 256 steuerfrei, sonst voll). Test: Golden 07.
- Mining oder Validator-Betrieb erzeugt einen Hinweis (möglicherweise gewerblich, außerhalb des Umfangs).
- Unter dem Reform-Regelsatz zählen Einkünfte mit Regime Neu zum Kapitalertrags-Topf.
- *Noch nicht umgesetzt:* Bewertung zum Claiming-Zeitpunkt (BMF-Vereinfachungsregel) als Einstellung.

## Jahresergebnis § 23 und Verlustverrechnung (`Engine/YearlyAggregator.cs`)

- Gewinne und Verluste der Disposals **innerhalb der Frist**, je Asset und Konto aufgeschlüsselt; Disposals nach Fristablauf werden vollständig aufgelistet, aber als steuerfrei ausgewiesen („steuerfrei nach Frist“).
- **Freigrenze:** Gesamtgewinn (Disposals plus Eingabe „andere private Veräußerungsgeschäfte“, z. B. Gold) unter 1.000 EUR (ab VZ 2024) → steuerfrei; ab 1.000 EUR ist der **gesamte** Betrag steuerpflichtig (Freigrenze, kein Freibetrag). Tests: Golden 08a (999,99) und 08b (1.000,00).
- **Verlustvortrag** (Eingabe aus dem Bescheid über die gesonderte Feststellung): bei Gesamtverlust wächst der Vortrag um den Verlust; bei Gewinn über der Freigrenze wird er bis zur Höhe des Gewinns verrechnet. Tests: Golden 09a, 09b.
  *Annahme:* Die Freigrenze wird auf den Gesamtgewinn **vor** Abzug des Verlustvortrags geprüft; bleibt der Gewinn darunter, bleibt der Vortrag unverändert erhalten. Bitte mit Beraterin bestätigen.
- Verluste aus Krypto-Veräußerungen sind nur mit Gewinnen aus privaten Veräußerungsgeschäften verrechenbar.

## Futures (Bitget), Termingeschäfte

- Eigenes Modul, **nie mit Spot-Lots vermischt**, keine Haltefrist. Realisierter Gewinn/Verlust, Funding, Gebühren und Liquidationen werden je Buchung mit dem EUR-Kurs des Buchungstags (USDT→EUR) umgerechnet und je Steuerjahr summiert. Test: Golden 16.
- Fehlt der Kurs, entsteht **eine** Sammelmeldung je Coin mit Anzahl und Zeitraum; diese Buchungen fehlen dann in der Summe (nie 0 EUR).
- Zuordnung: Einkünfte aus Kapitalvermögen (Termingeschäfte), Ausweis in **Anlage KAP**, getrennt von § 23. Bei ausländischen Anbietern ohne Steuerabzug ist der Eintrag in der Anlage KAP nötig.
  **Verlustverrechnungsregeln für Termingeschäfte vor Abgabe mit einer Beraterin prüfen**; die App wendet keine Verlustverrechnungsbeschränkung an.
- Hinweis zur Methode: Die Summe der Futures-Buchungen ist die Veränderung des Futures-Kontos; interne Umbuchungen (Transfers Spot ↔ Futures, Margin-Umverteilung) sind nicht steuerrelevant und werden ignoriert.

## Reform-Entwurf 2027 (nicht beschlossen)

Nur wenn der Schalter an ist, für Lots mit Regime Neu: keine Haltefrist, Einkünfte aus Kapitalvermögen, 25 % + 5,5 % Soli (26,375 %), Sparer-Pauschbetrag 1.000 / 2.000 EUR (Einzel-/Zusammenveranlagung), Kirchensteuer optional (noch nicht berechnet). Alt-Lots bleiben im alten Recht.
Tests: Golden 13a/13b (Stichtag), 14a/14b (Alt und Neu in einer Wallet).

## Befunde (Review-Queue)

Die Engine meldet, was sie nicht sicher rechnen kann: fehlender Kurs, negativer Bestand, Transfers ohne Gegenstück, Buchungen mit Status `NeedsReview`, nicht unterstützte Assets, Schenkungen (brauchen Angaben des Vorbesitzers), Mining-Hinweis. Ein Jahr sollte erst als „final“ gelten, wenn die Befunde gelöst sind (P11).

## Golden Tests (P19) und ADR

`tests/TaxEngine.Tests/Golden/` enthält **22 Szenarien** als JSON (Eingabebuchungen und erwartetes Ergebnis) mit der Hand-Rechnung als Markdown daneben:
01 Kauf/Verkauf in 1 Jahr, 02 nach 1 Jahr + 1 Tag, 03 am Jahrestag, 04 Teilverkauf über drei Lots (FIFO), 05 Tausch BTC→ETH→USDT, 06 Transfer mit Gebühr, 07 Staking, 08a/b Freigrenze,
09a/b Verluste und Vortrag, 10 Gebühr in drittem Coin, 11 Wrapping, 12 Schaltjahr, 13a/b Stichtag 2027 mit Reform AUS/AN, 14a/b Alt-/Neu-Lots, 15 Negativer Bestand, 16 Futures,
17 Durchschnittsmethode, 18 Transfer ohne Gegenstück und fehlender Kurs. Die Golden Tests laufen in der CI. Eine Änderung des erwarteten Ergebnisses braucht einen ADR ([ADR 0003](adr/0003-golden-tests.md)).
Die Zeilenabdeckung der TaxEngine liegt bei 99 % (CI-Mindestwert 95 %).

## Annahmen und offene Punkte

- Rechtsstand und BMF-Rn. sind aus dem Katalog übernommen, nicht online geprüft.
- EUR-Kurse sind Eingabe der Engine (Kursdienst: eigener Schritt, P21). Buchungen aus Trade Republic tragen ihren EUR-Betrag selbst.
- Wertpapiere (Aktien, ETFs, Derivate) werden von der Krypto-Engine bewusst ignoriert (P20).
- Schenkungen/Erbschaften von Krypto, Wrapping-Ausnahmen je Paar, Claiming-Bewertung, Kirchensteuer: nicht umgesetzt.
