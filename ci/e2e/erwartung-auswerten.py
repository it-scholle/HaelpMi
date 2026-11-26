#!/usr/bin/env python3
"""Wertet einen E2E-Negativtest (ERWARTUNG=negativ) aus den trx-Dateien der Geräte aus.

Aufruf: erwartung-auswerten.py <ergebnis-verzeichnis> <geraete> <erwartet-rot> [produkt-ref]

  ergebnis-verzeichnis  enthält je Gerät das heruntergeladene Artefakt e2e-<gerät>/
                        (trx/*.trx und dotnet-test-exitcode.txt aus dem Test-Job)
  geraete               Geräteauswahl des Laufs, z. B. "a,b,c"
  erwartet-rot          kommagetrennte T-Kürzel, z. B. "T6,T10W". "T10" steht für jeden Fehlschlag eines
                        Tests T10_…, "T10W" nur für einen Fehlschlag von T10_…, dessen Meldung die Marke
                        "[T10W]" trägt (ein bestimmtes Fehlerbild des Tests).
  produkt-ref           nur für den Bericht

Urteil "Negativtest bestanden" (Exit 0) genau dann, wenn jedes Kürzel mindestens einen passenden
Fehlschlag hat und nichts Unerwartetes vorliegt. Ein Folgefehler ("Test tN ist auf der Gegenseite
gescheitert") zählt nie als Nachweis, nur als erlaubter Begleiter eines eigentlichen Fehlschlags
desselben Kürzels. Zeilen aus uebersprungen.txt (Teilprüfungen für Features, die der Altstand noch nicht
hat) stehen im Bericht und ändern das Urteil nicht. Unerwartet ist: ein Fehlschlag, der zu keinem Kürzel passt, jedes andere Ergebnis als Passed/Failed/NotExecuted, ein abgebrochener Testlauf, ein Gerät ohne
Artefakt oder Exit-Code, ein Exit-Code ungleich 0 ohne fehlgeschlagenen Test. Sonst Exit 1.
Die xunit-Konsolenzeile „[xUnit.net …] <Test> [FAIL]“, die vstest zusätzlich als RunInfo ablegt, zählt nur,
wenn das Ergebnis dieses Tests in der trx fehlt; widerspricht sie der trx, ist das unerwartet.
Exit 2 = Aufruf ungültig. Den Bericht (Markdown) schreibt das Skript auf stdout.
"""

from __future__ import annotations

import re
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field
from pathlib import Path

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
MELDUNG_MAX = 300
KUERZEL = re.compile(r"T([1-9][0-9]{0,2})([A-Z]?)")
TESTNAME = re.compile(r"(?:^|\.)T([0-9]{1,3})_[^.]*$")
# Meldung aus Rendezvous.WaitForAsync, wenn die Gegenseite ihren Teil desselben Tests abgebrochen hat.
FOLGEFEHLER = re.compile(r"Test t[0-9]+ ist auf der Gegenseite gescheitert")
# RunInfo-Text, den vstest für jeden fehlgeschlagenen xunit-Test zusätzlich schreibt.
XUNIT_FAIL = re.compile(r"\[xUnit\.net [0-9:.]+\]\s+(\S+) \[FAIL\]")


@dataclass(frozen=True)
class Kuerzel:
    text: str
    nummer: int
    variante: str

    def passt(self, nummer: int | None, meldung: str) -> bool:
        if nummer != self.nummer:
            return False
        return not self.variante or f"[{self.text}]" in meldung


@dataclass
class Auswertung:
    kuerzel: list[Kuerzel]
    erwartet: list[str] = field(default_factory=list)
    unerwartet: list[str] = field(default_factory=list)
    hinweise: list[str] = field(default_factory=list)
    uebersprungen: list[str] = field(default_factory=list)
    bestanden: int = 0
    getroffen: set[str] = field(default_factory=set)
    folgefehler: list[tuple[list[str], str]] = field(default_factory=list)

    def folgefehler_einordnen(self) -> None:
        """Ein Folgefehler ist nie selbst der Nachweis, nur erlaubt neben einem eigentlichen Fehlschlag desselben Kürzels."""
        for passende, zeile in self.folgefehler:
            if any(k in self.getroffen for k in passende):
                self.erwartet.append(f"{zeile} (Folgefehler zu {', '.join(passende)})")
            else:
                self.unerwartet.append(zeile + " (Folgefehler ohne eigentlichen Fehlschlag desselben Tests)")

    @property
    def fehlend(self) -> list[str]:
        return [k.text for k in self.kuerzel if k.text not in self.getroffen]

    @property
    def bestanden_urteil(self) -> bool:
        return not self.fehlend and not self.unerwartet


def kuerzel_lesen(text: str) -> list[Kuerzel] | None:
    teile = [t.strip() for t in text.split(",")]
    if not teile or any(not KUERZEL.fullmatch(t) for t in teile) or len(set(teile)) != len(teile):
        return None
    return [Kuerzel(t, int(m.group(1)), m.group(2)) for t in teile if (m := KUERZEL.fullmatch(t))]


def kurz(text: str) -> str:
    zeile = " ".join(text.split())
    zeile = zeile.replace("|", "\\|")
    return zeile if len(zeile) <= MELDUNG_MAX else zeile[: MELDUNG_MAX - 1] + "…"


def meldung_roh(ergebnis: ET.Element) -> str:
    knoten = ergebnis.find("t:Output/t:ErrorInfo/t:Message", NS)
    return knoten.text if knoten is not None and knoten.text else ""


def testnummer(name: str) -> int | None:
    treffer = TESTNAME.search(name)
    return int(treffer.group(1)) if treffer else None


def trx_auswerten(datei: Path, geraet: str, a: Auswertung) -> int:
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
    fehlgeschlagen = 0
    ausgaenge: dict[str, str] = {}
    for ergebnis in wurzel.iterfind("t:Results/t:UnitTestResult", NS):
        name = ergebnis.get("testName", "(ohne Namen)")
        ausgang = ergebnis.get("outcome", "")
        ausgaenge[name] = ausgang
        if ausgang == "Passed":
            a.bestanden += 1
        elif ausgang == "NotExecuted":
            a.hinweise.append(f"{geraet}: `{name}` nicht ausgeführt")
        elif ausgang == "Failed":
            fehlgeschlagen += 1
            roh = meldung_roh(ergebnis)
            fehlschlag_einordnen(a, f"{geraet}: `{name}` – {kurz(roh) if roh else '(keine Meldung)'}", name, roh)
        else:
            a.unerwartet.append(f"{geraet}: `{name}` mit Ergebnis „{ausgang or 'unbekannt'}“")

    # Testhost-Absturz oder xunit "Catastrophic failure" setzt in der trx nur outcome="Failed" und eine
    # RunInfo mit Error; die danach geplanten Tests fehlen. Ohne diese Prüfung gälte der Lauf als bestanden.
    for info in wurzel.iterfind("t:ResultSummary/t:RunInfos/t:RunInfo", NS):
        if info.get("outcome") != "Error":
            continue
        text = info.find("t:Text", NS)
        roh = text.text.strip() if text is not None and text.text else ""
        konsole = XUNIT_FAIL.fullmatch(roh)
        if konsole is None:
            a.unerwartet.append(f"{geraet}: Fehler im Testlauf – {kurz(roh) if roh else '(keine Meldung)'}")
            continue
        # vstest legt für jeden fehlgeschlagenen Test zusätzlich dessen xunit-Konsolenzeile als RunInfo ab.
        name = konsole.group(1)
        if ausgaenge.get(name) == "Failed":
            continue
        if name in ausgaenge:
            a.unerwartet.append(f"{geraet}: `{name}` laut Konsole [FAIL], in {datei.name} aber „{ausgaenge[name] or 'unbekannt'}“")
            continue
        # Fallback: Ergebnis fehlt in der trx. Ohne Meldung kann es keine Fehlerbild-Marke (T10W) belegen.
        fehlgeschlagen += 1
        fehlschlag_einordnen(a, f"{geraet}: `{name}` – nur Konsolenzeile [FAIL], kein Ergebnis in {datei.name}", name, "")
    return fehlgeschlagen


def fehlschlag_einordnen(a: Auswertung, zeile: str, name: str, meldung: str) -> None:
    passende = [k.text for k in a.kuerzel if k.passt(testnummer(name), meldung)]
    if passende and FOLGEFEHLER.search(meldung):
        a.folgefehler.append((passende, zeile))
    elif passende:
        a.getroffen.update(passende)
        a.erwartet.append(f"{zeile} ({', '.join(passende)})")
    else:
        a.unerwartet.append(zeile + " (passt zu keinem Kürzel in ERWARTET_ROT)")


def geraet_auswerten(verzeichnis: Path, geraet: str, a: Auswertung) -> None:
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

    # Teilprüfungen für Features, die der Altstand noch nicht hat (Altstand.cs); ändern das Urteil nicht.
    protokoll = artefakt / "uebersprungen.txt"
    if protokoll.is_file():
        for zeile in protokoll.read_text(encoding="utf-8-sig").splitlines():
            if zeile.strip():
                a.uebersprungen.append(f"{geraet}: {kurz(zeile)}")

    trx_dateien = sorted((artefakt / "trx").rglob("*.trx")) if (artefakt / "trx").is_dir() else []
    fehlgeschlagen = sum(trx_auswerten(d, geraet, a) for d in trx_dateien)

    if not trx_dateien:
        if exitcode == 0:
            a.hinweise.append(f"{geraet}: keine Tests ausgewählt (keine trx-Datei, Exit-Code 0)")
        elif exitcode is not None:
            a.unerwartet.append(f"{geraet}: keine trx-Datei, dotnet test endete mit Exit-Code {exitcode}")
        return

    if exitcode not in (None, 0) and fehlgeschlagen == 0:
        a.unerwartet.append(f"{geraet}: dotnet test endete mit Exit-Code {exitcode}, aber ohne fehlgeschlagenen Test")


def bericht(a: Auswertung, geraete: list[str], produkt_ref: str) -> str:
    urteil = "bestanden: erwarteter Fehler nachgewiesen ✔" if a.bestanden_urteil else "NICHT bestanden ✘"
    zeilen = [
        f"### Negativtest (erwartet: Fehler) – PRODUKT_REF={produkt_ref or '(dieser Stand)'}",
        "",
        f"**Gesamturteil: Negativtest {urteil}**",
        "",
        "Negativtest gegen `produkt_ref`: prüft nicht den Stand dieses Branches und zählt nie als E2E-Gate.",
        "",
        f"- Geräte: {', '.join(geraete)}",
        f"- ERWARTET_ROT: `{','.join(k.text for k in a.kuerzel)}`",
        f"- bestanden: {a.bestanden}, erwartet fehlgeschlagen: {len(a.erwartet)}, unerwartet: {len(a.unerwartet)}",
        "",
    ]
    if a.fehlend:
        zeilen += [
            f"Grund: Kein Fehlschlag für {', '.join(a.fehlend)} – der Regressionstest erkennt den Fehler auf diesem Stand nicht.",
            "",
        ]
    if a.unerwartet:
        zeilen += ["Grund: unerwartete Ergebnisse (siehe unten).", ""]
    for titel, eintraege in (
        ("Erwartete Fehlschläge", a.erwartet),
        ("Unerwartete Ergebnisse", a.unerwartet),
        ("Übersprungene Prüfungen (Feature fehlt im Altstand)", a.uebersprungen),
        ("Hinweise", a.hinweise),
    ):
        zeilen.append(f"#### {titel}")
        zeilen += [f"- {e}" for e in eintraege] or ["- keine"]
        zeilen.append("")
    return "\n".join(zeilen)


def main(argumente: list[str]) -> int:
    if len(argumente) not in (3, 4):
        print(__doc__, file=sys.stderr)
        return 2
    verzeichnis = Path(argumente[0])
    geraete = [g for g in argumente[1].split(",") if g]
    produkt_ref = argumente[3] if len(argumente) == 4 else ""
    if not geraete or not all(re.fullmatch(r"[a-z]", g) for g in geraete):
        print(f"Ungültige Geräteauswahl: '{argumente[1]}'", file=sys.stderr)
        return 2
    kuerzel = kuerzel_lesen(argumente[2])
    if kuerzel is None:
        print(f"ERWARTET_ROT ungültig: '{argumente[2]}' (erwartet: T-Kürzel wie T6,T10W, ohne Doppelte)", file=sys.stderr)
        return 2
    if not re.fullmatch(r"[A-Za-z0-9._/-]*", produkt_ref):
        print("produkt-ref ungültig", file=sys.stderr)
        return 2

    a = Auswertung(kuerzel)
    for geraet in geraete:
        geraet_auswerten(verzeichnis, geraet, a)
    a.folgefehler_einordnen()
    print(bericht(a, geraete, produkt_ref))
    return 0 if a.bestanden_urteil else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
