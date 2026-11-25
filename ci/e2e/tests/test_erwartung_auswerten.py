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

    def auswerten(self, geraete: str, muster: str | None = None) -> tuple[int, str]:
        argumente = [str(self.pfad), geraete] + ([muster] if muster is not None else [])
        puffer = io.StringIO()
        with redirect_stdout(puffer):
            code = auswerten.main(argumente)
        return code, puffer.getvalue()

    def aufraeumen(self) -> None:
        self._tmp.cleanup()


GRUEN_A = trx([("GeraetATests.T1_Installation", "Passed", "")])
GRUEN_B = trx([("GeraetBTests.T4_Discovery", "Passed", "")])


class ErwartungRotTests(unittest.TestCase):
    def setUp(self) -> None:
        self.lauf = Lauf()

    def tearDown(self) -> None:
        self.lauf.aufraeumen()

    def test_erwarteter_fehlschlag_ist_gruen(self) -> None:
        self.lauf.geraet("a", "1", trx([
            ("GeraetATests.T1_Installation", "Passed", ""),
            ("GeraetATests.T22_Regression41_Banner", "Failed", "Kein Banner erschienen"),
        ], lauf="Failed"))
        self.lauf.geraet("b", "0", GRUEN_B)
        code, text = self.lauf.auswerten("a,b", "T22_")
        self.assertEqual(code, 0, text)
        self.assertIn("Fehler reproduziert ✔", text)
        self.assertIn("Kein Banner erschienen", text)

    def test_ohne_muster_zaehlt_jeder_fehlschlag(self) -> None:
        self.lauf.geraet("a", "1", trx([("GeraetATests.T22_X", "Failed", "kaputt")], lauf="Failed"))
        code, text = self.lauf.auswerten("a", "")
        self.assertEqual(code, 0, text)

    def test_alles_gruen_heisst_nicht_reproduziert(self) -> None:
        self.lauf.geraet("a", "0", GRUEN_A)
        self.lauf.geraet("b", "0", GRUEN_B)
        code, text = self.lauf.auswerten("a,b", "T22_")
        self.assertEqual(code, 1)
        self.assertIn("nicht reproduziert ✘", text)
        self.assertIn("Kein erwarteter Test", text)

    def test_alles_gruen_ohne_muster_heisst_nicht_reproduziert(self) -> None:
        self.lauf.geraet("a", "0", GRUEN_A)
        code, _ = self.lauf.auswerten("a")
        self.assertEqual(code, 1)

    def test_zusaetzlicher_unerwarteter_fehlschlag_ist_rot(self) -> None:
        self.lauf.geraet("a", "1", trx([
            ("GeraetATests.T22_Regression41_Banner", "Failed", "Kein Banner"),
            ("GeraetATests.T5_Alarm", "Failed", "Timeout"),
        ], lauf="Failed"))
        code, text = self.lauf.auswerten("a", "T22_")
        self.assertEqual(code, 1)
        self.assertIn("T5_Alarm", text)
        self.assertIn("passt nicht zu ERWARTET_ROT", text)

    def test_unerwarteter_fehlschlag_auf_anderem_geraet_ist_rot(self) -> None:
        self.lauf.geraet("a", "1", trx([("GeraetATests.T22_X", "Failed", "erwartet")], lauf="Failed"))
        self.lauf.geraet("b", "1", trx([("GeraetBTests.T4_Discovery", "Failed", "anders")], lauf="Failed"))
        code, _ = self.lauf.auswerten("a,b", "T22_")
        self.assertEqual(code, 1)

    def test_fehlendes_artefakt_ist_rot(self) -> None:
        self.lauf.geraet("a", "1", trx([("GeraetATests.T22_X", "Failed", "erwartet")], lauf="Failed"))
        code, text = self.lauf.auswerten("a,b", "T22_")
        self.assertEqual(code, 1)
        self.assertIn("Artefakt e2e-b fehlt", text)

    def test_fehlender_exitcode_ist_rot(self) -> None:
        self.lauf.geraet("a", None, trx([("GeraetATests.T22_X", "Failed", "erwartet")], lauf="Failed"))
        code, text = self.lauf.auswerten("a", "T22_")
        self.assertEqual(code, 1)
        self.assertIn("kein Exit-Code", text)

    def test_fehlende_trx_mit_exitcode_ungleich_null_ist_rot(self) -> None:
        self.lauf.geraet("a", "1", trx([("GeraetATests.T22_X", "Failed", "erwartet")], lauf="Failed"))
        self.lauf.geraet("b", "1", None)
        code, text = self.lauf.auswerten("a,b", "T22_")
        self.assertEqual(code, 1)
        self.assertIn("keine trx-Datei", text)

    def test_geraet_ohne_ausgewaehlte_tests_ist_neutral(self) -> None:
        self.lauf.geraet("a", "1", trx([("GeraetATests.T22_X", "Failed", "erwartet")], lauf="Failed"))
        self.lauf.geraet("b", "0", None)
        code, text = self.lauf.auswerten("a,b", "T22_")
        self.assertEqual(code, 0, text)
        self.assertIn("keine Tests ausgewählt", text)

    def test_abgebrochener_lauf_ist_rot(self) -> None:
        self.lauf.geraet("a", "1", trx([("GeraetATests.T22_X", "Failed", "erwartet")], lauf="Aborted"))
        code, text = self.lauf.auswerten("a", "T22_")
        self.assertEqual(code, 1)
        self.assertIn("Aborted", text)

    def test_laufmeldung_error_nach_erwartetem_fehlschlag_ist_rot(self) -> None:
        # vstest setzt bei Testhost-Absturz oder xunit "Catastrophic failure" nur outcome="Failed" plus RunInfo Error;
        # die restlichen Tests fehlen dann in der trx.
        self.lauf.geraet("a", "1", trx([("GeraetATests.T22_X", "Failed", "erwartet")], lauf="Failed",
                                        laufmeldung="The active test run was aborted. Reason: Test host process crashed"))
        code, text = self.lauf.auswerten("a", "T22_")
        self.assertEqual(code, 1, text)
        self.assertIn("Test host process crashed", text)

    def test_muster_auf_alle_tests_gibt_hinweis(self) -> None:
        self.lauf.geraet("a", "1", trx([
            ("HaelpMi.E2E.Tests.GeraetATests.T22_X", "Failed", "erwartet"),
            ("HaelpMi.E2E.Tests.GeraetATests.T1_Installation", "Passed", ""),
        ], lauf="Failed"))
        code, text = self.lauf.auswerten("a", "E2E")
        self.assertEqual(code, 0, text)
        self.assertIn("passt auf alle ausgeführten Tests", text)

    def test_exitcode_ohne_fehlschlag_ist_rot(self) -> None:
        self.lauf.geraet("a", "1", trx([("GeraetATests.T22_X", "Failed", "erwartet")], lauf="Failed"))
        self.lauf.geraet("b", "3", GRUEN_B)
        code, text = self.lauf.auswerten("a,b", "T22_")
        self.assertEqual(code, 1)
        self.assertIn("ohne fehlgeschlagenen Test", text)

    def test_anderes_ergebnis_als_failed_ist_rot(self) -> None:
        self.lauf.geraet("a", "1", trx([("GeraetATests.T22_X", "Timeout", "")], lauf="Failed"))
        code, _ = self.lauf.auswerten("a", "T22_")
        self.assertEqual(code, 1)

    def test_nicht_ausgefuehrt_ist_neutral(self) -> None:
        self.lauf.geraet("a", "1", trx([
            ("GeraetATests.T22_X", "Failed", "erwartet"),
            ("GeraetATests.T9_Lizenz", "NotExecuted", ""),
        ], lauf="Failed"))
        code, text = self.lauf.auswerten("a", "T22_")
        self.assertEqual(code, 0, text)
        self.assertIn("nicht ausgeführt", text)

    def test_kaputte_trx_ist_rot(self) -> None:
        self.lauf.geraet("a", "1", "<TestRun")
        code, _ = self.lauf.auswerten("a", "")
        self.assertEqual(code, 1)

    def test_ungueltiges_muster_bricht_ab(self) -> None:
        code, _ = self.lauf.auswerten("a", "T22_(")
        self.assertEqual(code, 2)

    def test_ungueltige_geraete_brechen_ab(self) -> None:
        code, _ = self.lauf.auswerten("a,../x", "")
        self.assertEqual(code, 2)


if __name__ == "__main__":
    unittest.main()
