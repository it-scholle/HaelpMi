"""Tests für ci/e2e/erwartung-auswerten.py. Start: python3 -m unittest discover -s ci/e2e/tests"""

from __future__ import annotations

import importlib.util
import io
import sys
import tempfile
import unittest
from contextlib import redirect_stdout
from pathlib import Path

SKRIPT = Path(__file__).resolve().parent.parent / "erwartung-auswerten.py"
_spec = importlib.util.spec_from_file_location("erwartung_auswerten", SKRIPT)
assert _spec and _spec.loader
auswerten = importlib.util.module_from_spec(_spec)
# dataclass braucht das Modul in sys.modules.
sys.modules[_spec.name] = auswerten
_spec.loader.exec_module(auswerten)

NS = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"


def trx(ergebnisse: list[tuple[str, str, str]], lauf: str = "Completed", laufmeldung: str = "") -> str:
    zeilen = []
    for name, ausgang, text in ergebnisse:
        fehler = f"<Output><ErrorInfo><Message>{text}</Message></ErrorInfo></Output>" if text else ""
        zeilen.append(f'<UnitTestResult testName="{name}" outcome="{ausgang}">{fehler}</UnitTestResult>')
    return (
        f'<?xml version="1.0" encoding="utf-8"?><TestRun xmlns="{NS}">'
        f"<Results>{''.join(zeilen)}</Results>"
        f'<ResultSummary outcome="{lauf}"><Counters total="{len(ergebnisse)}" />'
        + (f'<RunInfos><RunInfo computerName="VM" outcome="Error" timestamp="2026-11-25T21:00:00">'
           f"<Text>{laufmeldung}</Text></RunInfo></RunInfos>" if laufmeldung else "")
        + "</ResultSummary></TestRun>"
    )


class Lauf:
    def __init__(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.pfad = Path(self._tmp.name)

    def geraet(self, g: str, exitcode: str | None, inhalt: str | None) -> None:
        d = self.pfad / f"e2e-{g}"
        d.mkdir()
        if exitcode is not None:
            # Wie Set-Content in Windows PowerShell: mit BOM und Zeilenende.
            (d / "dotnet-test-exitcode.txt").write_text(exitcode + "\r\n", encoding="utf-8-sig")
        if inhalt is not None:
            (d / "trx").mkdir()
            (d / "trx" / f"e2e-{g.upper()}.trx").write_text(inhalt, encoding="utf-8")

    def auswerten(self, geraete: str, erwartet_rot: str, produkt_ref: str = "v0.47.32") -> tuple[int, str]:
        argumente = [str(self.pfad), geraete, erwartet_rot, produkt_ref]
        puffer = io.StringIO()
        with redirect_stdout(puffer):
            code = auswerten.main(argumente)
        return code, puffer.getvalue()

    def aufraeumen(self) -> None:
        self._tmp.cleanup()


GRUEN_A = trx([("GeraetATests.T1_Installation", "Passed", "")])
GRUEN_B = trx([("GeraetBTests.T4_Discovery", "Passed", "")])


class NegativtestTests(unittest.TestCase):
    def setUp(self) -> None:
        self.lauf = Lauf()

    def tearDown(self) -> None:
        self.lauf.aufraeumen()

    def test_erwarteter_fehlschlag_ist_gruen(self) -> None:
        self.lauf.geraet("a", "1", trx([
            ("GeraetATests.T1_Installation", "Passed", ""),
            ("HaelpMi.E2E.Tests.GeraetATests.T22_Regression41_Banner", "Failed", "Kein Banner erschienen"),
        ], lauf="Failed"))
        self.lauf.geraet("b", "0", GRUEN_B)
        code, text = self.lauf.auswerten("a,b", "T22")
        self.assertEqual(code, 0, text)
        self.assertIn("Negativtest (erwartet: Fehler) – PRODUKT_REF=v0.47.32", text)
        self.assertIn("Negativtest bestanden", text)
        self.assertIn("Kein Banner erschienen", text)

    def test_uebersprungene_pruefungen_stehen_im_bericht_und_aendern_nichts(self) -> None:
        self.lauf.geraet("a", "1", trx([
            ("GeraetATests.T1_Installation", "Passed", ""),
            ("GeraetATests.T22_Regression41_Banner", "Failed", "Kein Banner erschienen"),
        ], lauf="Failed"))
        (self.lauf.pfad / "e2e-a" / "uebersprungen.txt").write_text(
            "T1: übersprungen: Betriebs-Log agent.log – Feature erst ab v0.47.35\r\n", encoding="utf-8-sig")
        self.lauf.geraet("b", "0", GRUEN_B)
        code, text = self.lauf.auswerten("a,b", "T22")
        self.assertEqual(code, 0, text)
        self.assertIn("#### Übersprungene Prüfungen", text)
        self.assertIn("a: T1: übersprungen: Betriebs-Log agent.log – Feature erst ab v0.47.35", text)

    def test_uebersprungene_pruefung_ersetzt_keinen_fehlschlag(self) -> None:
        self.lauf.geraet("a", "0", GRUEN_A)
        (self.lauf.pfad / "e2e-a" / "uebersprungen.txt").write_text("T22: übersprungen: X\n", encoding="utf-8")
        self.lauf.geraet("b", "0", GRUEN_B)
        code, text = self.lauf.auswerten("a,b", "T22")
        self.assertEqual(code, 1, text)
        self.assertIn("Kein Fehlschlag für T22", text)

    def test_alles_gruen_ist_rot(self) -> None:
        self.lauf.geraet("a", "0", GRUEN_A)
        self.lauf.geraet("b", "0", GRUEN_B)
        code, text = self.lauf.auswerten("a,b", "T22")
        self.assertEqual(code, 1)
        self.assertIn("NICHT bestanden ✘", text)
        self.assertIn("Kein Fehlschlag für T22", text)

    def test_jedes_kuerzel_muss_fehlschlagen(self) -> None:
        self.lauf.geraet("a", "1", trx([
            ("GeraetATests.T6_X", "Failed", "kaputt"),
            ("GeraetATests.T22_Y", "Passed", ""),
        ], lauf="Failed"))
        code, text = self.lauf.auswerten("a", "T6,T22")
        self.assertEqual(code, 1, text)
        self.assertIn("Kein Fehlschlag für T22", text)

    def test_mehrere_kuerzel_auf_mehreren_geraeten(self) -> None:
        self.lauf.geraet("a", "1", trx([("GeraetATests.T6_X", "Failed", "kaputt")], lauf="Failed"))
        self.lauf.geraet("b", "1", trx([
            ("GeraetBTests.T10_Regression136_FensterBleibtZu", "Failed", "[T10W] Regression #136: 3 von 25"),
            ("GeraetBTests.T4_Discovery", "Passed", ""),
        ], lauf="Failed"))
        code, text = self.lauf.auswerten("a,b", "T6, T10W")
        self.assertEqual(code, 0, text)

    def test_nummer_trifft_nicht_laengere_nummer(self) -> None:
        # Früher als regulärer Ausdruck: "T2" traf auch T20 bis T29.
        self.lauf.geraet("a", "1", trx([("GeraetATests.T22_X", "Failed", "kaputt")], lauf="Failed"))
        code, text = self.lauf.auswerten("a", "T2")
        self.assertEqual(code, 1, text)
        self.assertIn("passt zu keinem Kürzel", text)

    def test_variante_ohne_marke_ist_rot(self) -> None:
        # T10 schlug fehl, aber nicht mit dem Fehlerbild W (z. B. Rendezvous-Timeout): kein Nachweis.
        self.lauf.geraet("b", "1", trx([
            ("GeraetBTests.T10_Regression136_FensterBleibtZu", "Failed", "Timeout beim Warten auf t10-3-aus"),
        ], lauf="Failed"))
        code, text = self.lauf.auswerten("b", "T10W")
        self.assertEqual(code, 1, text)
        self.assertIn("passt zu keinem Kürzel", text)
        self.assertIn("Kein Fehlschlag für T10W", text)

    def test_kuerzel_ohne_variante_nimmt_jede_meldung(self) -> None:
        self.lauf.geraet("b", "1", trx([("GeraetBTests.T10_R", "Failed", "irgendwas")], lauf="Failed"))
        code, text = self.lauf.auswerten("b", "T10")
        self.assertEqual(code, 0, text)

    def test_folgefehler_allein_ist_kein_nachweis(self) -> None:
        # T23 scheitert auf A nur, weil B aus einem fremden Grund abgebrochen hat; B's Fehlschlag passt nicht zu T22.
        self.lauf.geraet("a", "1", trx([
            ("GeraetATests.T23_X", "Failed", "Rendezvous-Marke 't23-b': Test t23 ist auf der Gegenseite gescheitert, siehe deren Ergebnis."),
        ], lauf="Failed"))
        code, text = self.lauf.auswerten("a", "T23")
        self.assertEqual(code, 1, text)
        self.assertIn("Folgefehler ohne eigentlichen Fehlschlag", text)
        self.assertIn("Kein Fehlschlag für T23", text)

    def test_folgefehler_neben_eigentlichem_fehlschlag_ist_erlaubt(self) -> None:
        self.lauf.geraet("a", "1", trx([
            ("GeraetATests.T23_X", "Failed", "Test t23 ist auf der Gegenseite gescheitert, siehe deren Ergebnis."),
        ], lauf="Failed"))
        self.lauf.geraet("b", "1", trx([("GeraetBTests.T23_Y", "Failed", "Empfänger hat das Popup nicht gezeigt")], lauf="Failed"))
        code, text = self.lauf.auswerten("a,b", "T23")
        self.assertEqual(code, 0, text)
        self.assertIn("Folgefehler zu T23", text)

    def test_folgefehler_zaehlt_nicht_fuer_variante(self) -> None:
        self.lauf.geraet("a", "1", trx([
            ("GeraetATests.T10_S", "Failed", "Test t10 ist auf der Gegenseite gescheitert"),
        ], lauf="Failed"))
        self.lauf.geraet("b", "1", trx([("GeraetBTests.T10_R", "Failed", "[T10W] Regression #136: 3 von 25")], lauf="Failed"))
        code, text = self.lauf.auswerten("a,b", "T10W")
        self.assertEqual(code, 1, text)
        self.assertIn("passt zu keinem Kürzel", text)

    def test_zusaetzlicher_unerwarteter_fehlschlag_ist_rot(self) -> None:
        self.lauf.geraet("a", "1", trx([
            ("GeraetATests.T22_Regression41_Banner", "Failed", "Kein Banner"),
            ("GeraetATests.T5_Alarm", "Failed", "Timeout"),
        ], lauf="Failed"))
        code, text = self.lauf.auswerten("a", "T22")
        self.assertEqual(code, 1)
        self.assertIn("T5_Alarm", text)
        self.assertIn("passt zu keinem Kürzel", text)

    def test_unerwarteter_fehlschlag_auf_anderem_geraet_ist_rot(self) -> None:
        self.lauf.geraet("a", "1", trx([("GeraetATests.T22_X", "Failed", "erwartet")], lauf="Failed"))
        self.lauf.geraet("b", "1", trx([("GeraetBTests.T4_Discovery", "Failed", "anders")], lauf="Failed"))
        code, _ = self.lauf.auswerten("a,b", "T22")
        self.assertEqual(code, 1)

    def test_fehlendes_artefakt_ist_rot(self) -> None:
        self.lauf.geraet("a", "1", trx([("GeraetATests.T22_X", "Failed", "erwartet")], lauf="Failed"))
        code, text = self.lauf.auswerten("a,b", "T22")
        self.assertEqual(code, 1)
        self.assertIn("Artefakt e2e-b fehlt", text)

    def test_fehlender_exitcode_ist_rot(self) -> None:
        self.lauf.geraet("a", None, trx([("GeraetATests.T22_X", "Failed", "erwartet")], lauf="Failed"))
        code, text = self.lauf.auswerten("a", "T22")
        self.assertEqual(code, 1)
        self.assertIn("kein Exit-Code", text)

    def test_fehlende_trx_mit_exitcode_ungleich_null_ist_rot(self) -> None:
        self.lauf.geraet("a", "1", trx([("GeraetATests.T22_X", "Failed", "erwartet")], lauf="Failed"))
        self.lauf.geraet("b", "1", None)
        code, text = self.lauf.auswerten("a,b", "T22")
        self.assertEqual(code, 1)
        self.assertIn("keine trx-Datei", text)

    def test_geraet_ohne_ausgewaehlte_tests_ist_neutral(self) -> None:
        self.lauf.geraet("a", "1", trx([("GeraetATests.T22_X", "Failed", "erwartet")], lauf="Failed"))
        self.lauf.geraet("b", "0", None)
        code, text = self.lauf.auswerten("a,b", "T22")
        self.assertEqual(code, 0, text)
        self.assertIn("keine Tests ausgewählt", text)

    def test_abgebrochener_lauf_ist_rot(self) -> None:
        self.lauf.geraet("a", "1", trx([("GeraetATests.T22_X", "Failed", "erwartet")], lauf="Aborted"))
        code, text = self.lauf.auswerten("a", "T22")
        self.assertEqual(code, 1)
        self.assertIn("Aborted", text)

    def test_laufmeldung_error_nach_erwartetem_fehlschlag_ist_rot(self) -> None:
        # vstest setzt bei Testhost-Absturz oder xunit "Catastrophic failure" nur outcome="Failed" plus RunInfo Error;
        # die restlichen Tests fehlen dann in der trx.
        self.lauf.geraet("a", "1", trx([("GeraetATests.T22_X", "Failed", "erwartet")], lauf="Failed",
                                        laufmeldung="The active test run was aborted. Reason: Test host process crashed"))
        code, text = self.lauf.auswerten("a", "T22")
        self.assertEqual(code, 1, text)
        self.assertIn("Test host process crashed", text)

    def test_exitcode_ohne_fehlschlag_ist_rot(self) -> None:
        self.lauf.geraet("a", "1", trx([("GeraetATests.T22_X", "Failed", "erwartet")], lauf="Failed"))
        self.lauf.geraet("b", "3", GRUEN_B)
        code, text = self.lauf.auswerten("a,b", "T22")
        self.assertEqual(code, 1)
        self.assertIn("ohne fehlgeschlagenen Test", text)

    def test_anderes_ergebnis_als_failed_ist_rot(self) -> None:
        self.lauf.geraet("a", "1", trx([("GeraetATests.T22_X", "Timeout", "")], lauf="Failed"))
        code, _ = self.lauf.auswerten("a", "T22")
        self.assertEqual(code, 1)

    def test_nicht_ausgefuehrt_ist_neutral(self) -> None:
        self.lauf.geraet("a", "1", trx([
            ("GeraetATests.T22_X", "Failed", "erwartet"),
            ("GeraetATests.T9_Lizenz", "NotExecuted", ""),
        ], lauf="Failed"))
        code, text = self.lauf.auswerten("a", "T22")
        self.assertEqual(code, 0, text)
        self.assertIn("nicht ausgeführt", text)

    def test_kaputte_trx_ist_rot(self) -> None:
        self.lauf.geraet("a", "1", "<TestRun")
        code, _ = self.lauf.auswerten("a", "T22")
        self.assertEqual(code, 1)

    def test_ungueltige_kuerzel_brechen_ab(self) -> None:
        for wert in ("", "T22_", "T22|T23", "t22", "T1234", "T10WW", "T010", "T0", "T6,T6", "T6,", "E2E", ".*", "T6;rm"):
            with self.subTest(wert=wert):
                code, _ = self.lauf.auswerten("a", wert)
                self.assertEqual(code, 2)

    def test_ungueltige_geraete_brechen_ab(self) -> None:
        code, _ = self.lauf.auswerten("a,../x", "T22")
        self.assertEqual(code, 2)

    def test_ungueltiger_produkt_ref_bricht_ab(self) -> None:
        code, _ = self.lauf.auswerten("a", "T22", "v1`x")
        self.assertEqual(code, 2)


if __name__ == "__main__":
    unittest.main()
