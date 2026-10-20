#!/usr/bin/env python3
"""Prüft die Zeilenabdeckung eines Assemblies aus Cobertura-Berichten.

Aufruf: check-coverage.py <verzeichnis> <assembly> <mindest-prozent>

Mehrere Testprojekte können dasselbe Assembly laden. Deshalb werden die Zeilen über alle Berichte zusammengeführt
(je Datei und Zeilennummer zählt die höchste Trefferzahl). Ohne ausführbare Zeilen im Assembly (z. B. nur Gerüst)
gilt die Prüfung als bestanden.
"""
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def main() -> int:
    root, assembly, minimum = Path(sys.argv[1]), sys.argv[2], float(sys.argv[3])
    hits: dict[tuple[str, int], int] = {}
    for report in root.rglob("coverage.cobertura.xml"):
        for package in ET.parse(report).getroot().iter("package"):
            if package.get("name") != assembly:
                continue
            for cls in package.iter("class"):
                filename = cls.get("filename", "")
                for line in cls.iter("line"):
                    key = (filename, int(line.get("number", "0")))
                    hits[key] = max(hits.get(key, 0), int(line.get("hits", "0")))
    total = len(hits)
    covered = sum(1 for h in hits.values() if h > 0)
    if total == 0:
        print(f"{assembly}: keine Zeilen gemessen, Prüfung übersprungen.")
        return 0
    percent = 100 * covered / total
    print(f"{assembly}: {percent:.1f} % Zeilenabdeckung (Minimum {minimum:.0f} %), {covered}/{total} Zeilen")
    return 0 if percent >= minimum else 1


if __name__ == "__main__":
    sys.exit(main())
