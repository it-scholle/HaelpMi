# 0003 – Update-Rollout: einmalige Freigabe, Wellen, Self-Bootstrap

- Datum: 2026-08-11 (einmalige Freigabe), 2026-08-16 (Wellen-Rollout, Self-Bootstrap), 2026-09-22 (inaktiv auf `release-1.0-MVP`)
- Status: angenommen; Self-Bootstrap nur im Legacy-Stand umgesetzt
- Entschieden von: Repo-Owner

## Kontext
Der Umsetzungs-Prompt Teil 2 (Abschnitt 11) sah ein gestaffeltes Freigabekontingent vor, bei dem
der Admin Stufe für Stufe freigibt (1 → 2 → 4 → 8 Geräte). Das war in der Bedienung umständlich.
Ein Schlag auf die gesamte Kundengruppe birgt bei einer fehlerhaften Version aber ein großes
Risiko. Außerdem konnte die allererste Maschine einer Gruppe über die P2P-Kaskade nie
aktualisiert werden, weil das Wellen-Gate einen bereits aktualisierten Peer voraussetzt.

## Entscheidung
- Der Admin gibt jede Version genau einmal frei; danach verbreitet sich das Update automatisch P2P
  (jedes erfolgreich aktualisierte Gerät gibt es beim nächsten Boot-Call weiter). Keine Stufen-UI.
- Die Wellenbreite ergibt sich automatisch: Ein Gerät aktualisiert erst, wenn laut eigenem
  P2P-Wissen mindestens so viele Peers die Version haben, wie es selbst in der deterministischen
  Warteschlange vor sich hat (`UpdateOrchestrator.IsMyTurn`). Kein zentraler Zähler.
- Unverändert: lokaler Testping und mindestens eine Peer-Bestätigung vor jeder Übernahme, Jitter,
  Retry-Obergrenze, Fehlermeldung an den Admin, Not-Aus pro Kreis.
- Self-Bootstrap (Legacy): ein vom Install-Creator erzeugter `HaelpMi.UpdateBootstrapper`
  aktualisiert die erste Maschine direkt gegen den lokalen Update-Dienst. Ein automatischer
  Mailversand an Kunden war vorgemerkt, aber nicht umgesetzt.
- Auf `release-1.0-MVP` ist die gesamte Update-Pipeline inaktiv (Code als
  `DEPRECATED für release-1.0-MVP` markiert, Dienst wird nicht installiert); Updates erfolgen
  manuell per Installer. Reaktivierung in einem späteren Release über eigene Tickets.

## Folgen
- Abschnitt 11 des Umsetzungs-Prompts Teil 2 ist in diesem Punkt überholt.
- Sicherheits-Tickets zur Update-Pipeline (#122, #123) bleiben bis zur Reaktivierung offen.
