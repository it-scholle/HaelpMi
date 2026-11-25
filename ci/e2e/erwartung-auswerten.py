#!/usr/bin/env python3
"""Wertet einen E2E-Messlauf (ERWARTUNG=rot) aus den trx-Dateien der Geräte aus.

Aufruf: erwartung-auswerten.py <ergebnis-verzeichnis> <geraete> [muster]

  ergebnis-verzeichnis  enthält je Gerät das heruntergeladene Artefakt e2e-<gerät>/
                        (trx/*.trx und dotnet-test-exitcode.txt aus dem Test-Job)
  geraete               Geräteauswahl des Laufs, z. B. "a,b,c"
  muster                regulärer Ausdruck (Python re.search) auf den vollen Testnamen;
                        leer = jeder fehlgeschlagene Test gilt als erwartet

Urteil "Fehler reproduziert" (Exit 0) nur, wenn mindestens ein erwarteter Test fehlschlägt und
nichts Unerwartetes vorliegt. Unerwartet ist: ein fehlgeschlagener Test außerhalb des Musters,
jedes andere Ergebnis als Passed/Failed/NotExecuted, ein abgebrochener Testlauf, ein Gerät ohne
Artefakt oder Exit-Code, ein Exit-Code ungleich 0 ohne fehlgeschlagenen Test. Sonst Exit 1.
Exit 2 = Aufruf ungültig. Den Bericht (Markdown) schreibt das Skript auf stdout.
"""

from __future__ import annotations

import re
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field
from pathlib import Path

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
NEUTRAL = {"Passed", "NotExecuted"}
MELDUNG_MAX = 300


@dataclass
class Auswertung:
    erwartet: list[str] = field(default_factory=list)
    unerwartet: list[str] = field(default_factory=list)
    hinweise: list[str] = field(default_factory=list)
    bestanden: int = 0
    ausgefuehrt: list[str] = field(default_factory=list)

    @property
    def reproduziert(self) -> bool:
        return bool(self.erwartet) and not self.unerwartet


def kurz(text: str) -> str:
    zeile = " ".join(text.split())
    zeile = zeile.replace("|", "\\|")
    return zeile if len(zeile) <= MELDUNG_MAX else zeile[: MELDUNG_MAX - 1] + "…"


def meldung(ergebnis: ET.Element) -> str:
    knoten = ergebnis.find("t:Output/t:ErrorInfo/t:Message", NS)
    return kurz(knoten.text or "") if knoten is not None and knoten.text else "(keine Meldung)"


def trx_auswerten(datei: Path, geraet: str, muster: re.Pattern[str] | None, a: Auswertung) -> int:
    """Gibt die Zahl der fehlgeschlagenen Tests in der Datei zurück."""
    try:
        wurzel = ET.parse(datei).getroot()
    except ET.ParseError as fehler:
        a.unerwartet.append(f"{geraet}: {datei.name} nicht lesbar ({kurz(str(fehler))})")
        return 0

    zusammenfassung = wurzel.find("t:ResultSummary", NS)
    lauf = zusammenfassung.get("outcome", "") if zusammenfassung is not None else ""
    if lauf not in ("Completed", "Failed"):
        a.unerwartet.append(f"{geraet}: Testlauf in {datei.name} endete mit „{lauf or 'unbekannt'}“")
    # Testhost-Absturz oder xunit "Catastrophic failure" setzt in der trx nur outcome="Failed" und eine
    # RunInfo mit Error; die danach geplanten Tests fehlen. Ohne diese Prüfung gälte der Lauf als reproduziert.
    for info in wurzel.iterfind("t:ResultSummary/t:RunInfos/t:RunInfo", NS):
        if info.get("outcome") == "Error":
            text = info.find("t:Text", NS)
            a.unerwartet.append(f"{geraet}: Fehler im Testlauf – {kurz(text.text or '') if text is not None and text.text else '(keine Meldung)'}")

    fehlgeschlagen = 0
    for ergebnis in wurzel.iterfind("t:Results/t:UnitTestResult", NS):
        name = ergebnis.get("testName", "(ohne Namen)")
        ausgang = ergebnis.get("outcome", "")
        if ausgang in ("Passed", "Failed"):
            a.ausgefuehrt.append(name)
        if ausgang == "Passed":
            a.bestanden += 1
        elif ausgang == "NotExecuted":
            a.hinweise.append(f"{geraet}: `{name}` nicht ausgeführt")
        elif ausgang == "Failed":
            fehlgeschlagen += 1
            zeile = f"{geraet}: `{name}` – {meldung(ergebnis)}"
            if muster is None or muster.search(name):
                a.erwartet.append(zeile)
            else:
                a.unerwartet.append(zeile + " (passt nicht zu ERWARTET_ROT)")
        else:
            a.unerwartet.append(f"{geraet}: `{name}` mit Ergebnis „{ausgang or 'unbekannt'}“")
    return fehlgeschlagen


def geraet_auswerten(verzeichnis: Path, geraet: str, muster: re.Pattern[str] | None, a: Auswertung) -> None:
    artefakt = verzeichnis / f"e2e-{geraet}"
    if not artefakt.is_dir():
        a.unerwartet.append(f"{geraet}: Artefakt e2e-{geraet} fehlt (Test-Job nicht gelaufen?)")
        return

    exitcode_datei = artefakt / "dotnet-test-exitcode.txt"
    exitcode_text = exitcode_datei.read_text(encoding="utf-8-sig").strip() if exitcode_datei.is_file() else ""
    if not re.fullmatch(r"-?\d+", exitcode_text):
        a.unerwartet.append(f"{geraet}: kein Exit-Code von dotnet test (Testschritt nicht bis zum Ende gelaufen?)")
        exitcode = None
    else:
        exitcode = int(exitcode_text)

    trx_dateien = sorted((artefakt / "trx").rglob("*.trx")) if (artefakt / "trx").is_dir() else []
    fehlgeschlagen = sum(trx_auswerten(d, geraet, muster, a) for d in trx_dateien)

    if not trx_dateien:
        if exitcode == 0:
            a.hinweise.append(f"{geraet}: keine Tests ausgewählt (keine trx-Datei, Exit-Code 0)")
        elif exitcode is not None:
            a.unerwartet.append(f"{geraet}: keine trx-Datei, dotnet test endete mit Exit-Code {exitcode}")
        return

    if exitcode not in (None, 0) and fehlgeschlagen == 0:
        a.unerwartet.append(f"{geraet}: dotnet test endete mit Exit-Code {exitcode}, aber ohne fehlgeschlagenen Test")


def bericht(a: Auswertung, geraete: list[str], muster_text: str) -> str:
    urteil = "Fehler reproduziert ✔" if a.reproduziert else "Fehler nicht reproduziert ✘"
    zeilen = [
        "### Messlauf (ERWARTUNG=rot)",
        "",
        f"**Gesamturteil: {urteil}**",
        "",
        "Messlauf gegen `produkt_ref`: prüft nicht den Stand dieses Branches und zählt nie als E2E-Gate.",
        "",
        f"- Geräte: {', '.join(geraete)}",
        f"- ERWARTET_ROT: `{muster_text}`" if muster_text else "- ERWARTET_ROT: (leer, jeder Fehlschlag gilt als erwartet)",
        f"- bestanden: {a.bestanden}, erwartet fehlgeschlagen: {len(a.erwartet)}, unerwartet: {len(a.unerwartet)}",
        "",
    ]
    if not a.reproduziert:
        if not a.erwartet:
            grund = "Kein erwarteter Test ist fehlgeschlagen: der Fehler trat auf diesem Stand nicht auf."
        else:
            grund = "Der Fehler trat auf, aber es gab zusätzlich unerwartete Ergebnisse (siehe unten)."
        zeilen += [f"Grund: {grund}", ""]
    for titel, eintraege in (
        ("Erwartete Fehlschläge", a.erwartet),
        ("Unerwartete Ergebnisse", a.unerwartet),
        ("Hinweise", a.hinweise),
    ):
        zeilen.append(f"#### {titel}")
        zeilen += [f"- {e}" for e in eintraege] or ["- keine"]
        zeilen.append("")
    return "\n".join(zeilen)


def main(argumente: list[str]) -> int:
    if len(argumente) not in (2, 3):
        print(__doc__, file=sys.stderr)
        return 2
    verzeichnis = Path(argumente[0])
    geraete = [g for g in argumente[1].split(",") if g]
    muster_text = argumente[2] if len(argumente) == 3 else ""
    if not geraete or not all(re.fullmatch(r"[a-z]", g) for g in geraete):
        print(f"Ungültige Geräteauswahl: '{argumente[1]}'", file=sys.stderr)
        return 2
    try:
        muster = re.compile(muster_text) if muster_text else None
    except re.error as fehler:
        print(f"ERWARTET_ROT ist kein gültiger regulärer Ausdruck: {fehler}", file=sys.stderr)
        return 2

    a = Auswertung()
    for geraet in geraete:
        geraet_auswerten(verzeichnis, geraet, muster, a)
    if muster is not None and len(a.ausgefuehrt) > 1 and all(muster.search(n) for n in a.ausgefuehrt):
        a.hinweise.append("ERWARTET_ROT passt auf alle ausgeführten Tests und wirkt damit wie ein leeres Muster")
    print(bericht(a, geraete, muster_text))
    return 0 if a.reproduziert else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
