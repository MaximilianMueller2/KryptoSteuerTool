#!/usr/bin/env python3
"""Prüft die Zeilenabdeckung eines Assemblies aus Cobertura-Berichten.

Aufruf: check-coverage.py <verzeichnis> <assembly> <mindest-prozent>
Ohne ausführbare Zeilen im Assembly (z. B. nur Gerüst) gilt die Prüfung als bestanden.
"""
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def main() -> int:
    root, assembly, minimum = Path(sys.argv[1]), sys.argv[2], float(sys.argv[3])
    covered = total = 0
    for report in root.rglob("coverage.cobertura.xml"):
        for package in ET.parse(report).getroot().iter("package"):
            if package.get("name") != assembly:
                continue
            for line in package.iter("line"):
                total += 1
                covered += 1 if int(line.get("hits", "0")) > 0 else 0
    if total == 0:
        print(f"{assembly}: keine Zeilen gemessen, Prüfung übersprungen.")
        return 0
    percent = 100 * covered / total
    print(f"{assembly}: {percent:.1f} % Zeilenabdeckung (Minimum {minimum:.0f} %)")
    return 0 if percent >= minimum else 1


if __name__ == "__main__":
    sys.exit(main())
