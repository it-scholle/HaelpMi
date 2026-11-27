---
id: handtest-0-48-0
titel: Handtest Release-Kandidat 0.48.0
kapitel: Entwicklung
reihenfolge: 21
zielgruppe: entwickler
ab_version: 0.48.0
schlagworte: [test, release, manuell, handtest]
siehe_auch: [entwickler-alpha-testplan, entwickler-bauen-und-installieren]
---

# Handtest Release-Kandidat 0.48.0

Abnahme-Checkliste für den Release-Kandidaten 0.48.0, der nach Freigabe als 1.0.0 veröffentlicht
wird. Sie ersetzt den Alpha-Testplan nicht, sondern zieht aus ihm heraus, was für 1.0 zählt, und
ergänzt die Neuerungen seit 0.46.x. Was die E2E-Suite schon prüft, steht hier nur, wenn es auf
echten Geräten anders aussehen kann (Ton, Vordergrund, Windows-Benachrichtigungen,
Firewall-Abfragen, Update-Weg).

Rückmeldung je Punkt: ✅ geht / ❌ geht nicht + was passiert ist (Text der Meldung, Screenshot,
`agent.log` und `audit.log` des betroffenen Geräts). Bei Abstürzen zusätzlich
`C:\ProgramData\HaelpMi\crash.log` (bei verbundenem Laufwerk `Z:` siehe Abschnitt 7).

**HälpMi neu starten** (in mehreren Punkten gebraucht): `HaelpMi.Agent.exe` im Task-Manager beenden
und danach entweder ab- und wieder anmelden oder `HaelpMi.Agent.exe` im Installationsordner
(`C:\Program Files\HaelpMi\`) per Doppelklick starten. Einen Startmenü-Eintrag dafür gibt es nicht.

## Aufbau

| Gerät | Rolle | Zweck |
|---|---|---|
| A | Admin | Dashboard, Sender |
| B | User | Empfänger 1 |
| C | User | Empfänger 2, später Update-Test |

- Drei Windows-10/11-Geräte (64 Bit) im selben Netz, Uhrzeit synchron.
- Lautsprecher bzw. Kopfhörer an B und C, Windows-Ton auf B einmal stummgeschaltet testen.
- Installer aus dem Release-Kandidaten mit dem Install-Creator gebaut (Kunden-Installer mit echter
  Kundengruppe, **nicht** Test-Installer), Lizenz für mindestens 3 Geräte.
- Kein Laufwerk `Z:` verbunden, außer es wird bewusst getestet (Punkt 7). Sonst liegen die Protokolle
  unter `C:\ProgramData\HaelpMi`.
- Profil „P1“: Sender A, Empfänger B und C, Schwelle 2. Profil „P2“: Sender A, Empfänger nur B,
  Schwelle 1. Profil „P3“: Sender A, keine Empfänger.

## 1. Erstinstallation und Firewall

- [ ] Admin-Installer auf A, User-Installer auf B und C ausführen. Raum und Raumnummer sind
      Pflicht (beide leer lassen → Fehlermeldung, kein Weiter).
- [ ] Während und nach der Installation erscheint **keine** Windows-Firewall-Abfrage.
- [ ] Direkt nach der Installation startet HälpMi (Tray-Symbol), **keine** Meldung „Installation
      unvollständig“.
- [ ] `wf.msc` → Eingehende Regeln: „HälpMi Agent“ und „HälpMi Konfiguration“ vorhanden, aktiviert,
      Aktion „Zulassen“.
- [ ] Gegenprobe Firewall-Altlast (auf C): HälpMi deinstallieren, in `wf.msc` von Hand eine
      eingehende **Block**-Regel für `HaelpMi.Agent.exe` im Installationsordner anlegen (so wie
      Windows sie nach einer abgebrochenen Abfrage anlegt), User-Installer erneut ausführen → die
      Block-Regel ist weg, C empfängt Alarme (Punkt 2).
- [ ] Nach spätestens einer Minute sehen sich A, B und C gegenseitig (Dashboard auf A, Reiter
      „Geräte / Lizenz“).
- [ ] Bei laufendem HälpMi `HaelpMi.Agent.exe` im Installationsordner ein zweites Mal starten →
      kein zweiter Prozess im Task-Manager, kein Fehlerdialog, im `agent.log` ein Eintrag, dass schon
      ein Agent läuft.

## 2. Alarmweg (P1: A → B und C, Schwelle 2)

- [ ] Tastenkürzel von P1 auf A → B und C zeigen das Alarmfenster **im Vordergrund**, auch über einer
      Vollbild-Anwendung (z. B. Video im Vollbild, PowerPoint-Präsentation).
- [ ] Das Fenster ist **sofort** bedienbar: Der Knopf „Bin unterwegs“ reagiert ohne Verzögerung auf
      Maus und Tastatur.
- [ ] **Erster Alarm nach dem Hochfahren:** B neu starten (Windows-Neustart, nicht nur HälpMi),
      anmelden, warten, bis das Tray-Symbol erscheint, dann ca. 10 s später P1 auf A auslösen →
      das erste Alarmfenster auf B ist **sofort** bedienbar (kein „hängendes“ Fenster, Ton läuft),
      und A zeigt B nach etwa einer Sekunde als empfangen. Dasselbe einmal nur mit HälpMi-Neustart
      auf B (siehe oben). Bei Verzögerung: `agent.log` von B mit `betriebslog.json` auf DEBUG
      (Abschnitt 7) wiederholen und beilegen, die Zeitmessungen „Alarmempfang“ zeigen, wo es hängt.
- [ ] Raum und Raumnummer groß, Benutzername klein.
- [ ] Ton auf B und C hörbar, auch bei stummgeschaltetem Windows-Ton.
- [ ] Sender-Fenster auf A unten rechts: „Empfangen: 2 von 2“ spätestens mit der zweiten Welle
      (wenige Sekunden).
- [ ] „Schließen“ ist auf B und C zunächst **deaktiviert**.
- [ ] **Schwelle:** B klickt „Bin unterwegs“ → „Schließen“ bleibt aus (1 von 2). C klickt „Bin
      unterwegs“ → auf B und C wird „Schließen“ frei, A zeigt beide Namen unter „Auf dem Weg“, die
      Wiederholung auf A stoppt.
- [ ] **Schließen:** B und C schließen ihr Fenster. Es öffnet sich **nicht** erneut, auch nicht in
      den nächsten 60 s (spätere Wellen desselben Alarms öffnen kein neues Fenster).
- [ ] **Abbruch:** Neuen Alarm P1 auslösen, niemand klickt. Auf A „Abbrechen“ → Wiederholung stoppt
      sofort, auf B und C wird „Schließen“ **sofort** frei; Schließen klappt, kein Wiederöffnen.
- [ ] **Ablauf:** Neuen Alarm P1 auslösen, niemand reagiert, volle Laufzeit (5 Minuten) abwarten → Wiederholung
      endet von selbst, Fenster auf B und C lassen sich danach schließen bzw. schließen sich
      spätestens 1 Minute nach dem letzten Signal.
- [ ] **Mehrfach-Nachwelle:** Alarm P1 auslösen. B klickt „Bin unterwegs“ und schließt **nicht**.
      C schließt sein Fenster erst, nachdem die Schwelle erreicht ist. Danach weitere 30 s warten →
      auf C erscheint kein zweites Fenster, auf B bleibt genau eines offen. Dasselbe zweimal direkt
      hintereinander mit zwei Alarmen (zweiter Alarm, während das Fenster des ersten noch offen ist)
      → je Alarm genau ein Fenster, jedes einzeln schließbar.
- [ ] **Empfangen erst nach Anzeige** (braucht zwei Windows-Konten auf B): Alarm P2 auslösen und
      sofort auf B „Benutzer wechseln“ → zweites Konto anmelden. Solange die Anmeldung läuft (noch
      kein Tray-Symbol), zeigt A „Empfangen: 0 von 1“. Sobald HälpMi in der neuen Sitzung läuft und
      das Fenster dort erscheint, springt der Zähler mit der nächsten Welle auf 1 von 1.
- [ ] **Gerät startet während des Alarms:** `HaelpMi.Agent.exe` auf C beenden, Alarm P1 auslösen,
      nach ca. 10 s auf C `HaelpMi.Agent.exe` im Installationsordner starten → C bekommt das Fenster
      mit der nächsten Welle, A zählt C mit.

## 3. Kein Empfänger (P3)

- [ ] Tastenkürzel von P3 auf A → Sender-Fenster zeigt sofort „Kein Empfänger eingestellt!“ und
      darunter „Info: Ein Admin muss im Dashboard Empfänger für dieses Profil einrichten.“ Kein
      Zähler, kein „Abbrechen“; das Fenster blendet sich nach ca. 6 s selbst aus.
- [ ] Ca. 30 s nach dem Drücken: Windows-Benachrichtigung „Wartung erforderlich“ mit „Ein
      Hintergrunddienst konnte eine Meldung nicht zustellen (Code H-00). Bitte die IT informieren.“
      - [ ] Absender lautet „Arbeitsplatz-Dienst“ (nicht „HälpMi“ oder „HaelpMi.Agent“), auf einem
            frisch installierten und auf einem von 0.46.x aktualisierten Gerät (Abschnitt 6).
      - [ ] Neben dem Absender steht ein neutrales Windows-Symbol, nicht das HälpMi-Logo.
            Screenshot.
      - [ ] Spätestens 60 s danach läuft kein `HaelpMi.Notify.exe` mehr (Task-Manager → Details).
- [ ] P3 dreimal innerhalb von 30 s drücken → genau **eine** Benachrichtigung.
- [ ] `audit.log` auf A: genau eine Welle je Druck, keine Wiederholung.
- [ ] Gerät B (in P3 nicht als Sender eingetragen) drückt das Tastenkürzel von P3 → **nichts**
      passiert, kein Fenster, keine Benachrichtigung.

## 4. Lizenz- und Sperrhinweise

- [ ] Im Dashboard auf A Gerät C deaktivieren → **kein** Banner „Lizenzkontingent überschritten“.
- [ ] Auf C das Tastenkürzel eines Profils drücken, in dem C Sender ist → eigenes Fenster im
      Vordergrund „Alarm nicht gesendet“ mit Grund „Gerät deaktiviert“ und Lösungsweg, nicht verdeckt.
- [ ] C wieder aktivieren → C sendet und empfängt wieder; im Reiter „Geräte / Lizenz“ zählt C normal
      gegen das Kontingent (kein Dauer-Override).
- [ ] Kontingent überschreiten (Lizenz für 2 Geräte einspielen oder viertes Gerät installieren) →
      überzähliges Gerät zeigt „Lizenz ausgeschöpft“ mit Lösungsweg, Alarme dort gesperrt.
- [ ] Lizenzablauf im laufenden Betrieb: Systemuhr auf A, B und C auf kurz vor das Ablaufdatum der
      Lizenz stellen, HälpMi **nicht** neu starten. Nach dem Ablauf (ohne Neustart): A zeigt den
      Ablauf-Hinweis, B und C zeigen einen Hinweis, Alarme laufen weiter. Vorher „läuft bald ab“
      prüfen. Uhr danach zurückstellen.
- [ ] Gültige Lizenz bleibt: Auf C eine ältere, abgelaufene Lizenz einspielen (falls vorhanden),
      während A eine gültige hat → nach dem nächsten Abgleich gilt auf allen Geräten die gültige.

## 5. Selbsttest und Konfiguration

- [ ] Konfigurationsprogramm auf B → Selbsttest → Erfolg.
- [ ] Auf einem Gerät ohne Alarm-Profil den Selbsttest starten → Meldung mit Grund und Lösungsweg,
      Admin-Gerät bietet das Dashboard an.
- [ ] Profil auf A umbenennen bzw. Tastenkürzel ändern → wirkt auf A sofort ohne Neustart und
      kommt auf B und C ohne Neustart an.

## 6. Update von 0.46.x

Vorbereitung: C auf den Ausgangszustand bringen (deinstallieren mit „Einstellungen entfernen“), dann
den 0.46.x-Installer derselben Kundengruppe installieren, Profil P2 so ändern, dass C Empfänger ist,
einen Alarm empfangen und schließen. Auf A ein Profil „P4“ anlegen: Sender C, Empfänger B,
Schwelle 1.

- [ ] Mischbetrieb vor dem Update: A (0.48.0) löst P2 aus → C (0.46.x) zeigt das Alarmfenster,
      „Bin unterwegs“ auf C → A zeigt „Empfangen: 1 von 1“ und C unter „Auf dem Weg“, Schließen
      auf C klappt. Rückrichtung: P4 auf C auslösen → B (0.48.0) zeigt das Alarmfenster, C zählt B
      als empfangen, Schließen auf B klappt.

- [ ] User-Installer 0.48.0 auf C ausführen (Update). Keine Raum-Abfrage, keine Firewall-Abfrage.
- [ ] Nach dem Update: genau ein `HaelpMi.Agent.exe`, Dateiversion 0.48.0 (Task-Manager → Details →
      Eigenschaften).
- [ ] Raum, Raumnummer, Profile und Lizenz unverändert; C taucht im Dashboard mit Version 0.48.0 auf.
- [ ] Alarm P1 von A → C empfängt, Schließen nach Schwelle klappt.
- [ ] Gerät, das unter 0.46.x deaktiviert und wieder aktiviert wurde: nach dem Update im Reiter
      „Geräte / Lizenz“ einmal aus- und wieder einschalten (Hinweis aus den Versionshinweisen) →
      zählt danach normal gegen das Kontingent.
- [ ] Mit zwei angemeldeten Windows-Benutzern auf C updaten: In der zweiten Sitzung läuft HälpMi
      danach nicht (bekannte Einschränkung); Ab- und wieder Anmelden in dieser Sitzung behebt es.
- [ ] Admin-Gerät A ebenso von 0.46.x auf 0.48.0 aktualisieren (falls A noch auf 0.46.x steht):
      Dashboard öffnet, alle Gruppen und Profile vorhanden.

## 7. Betriebs-Log

- [ ] `C:\ProgramData\HaelpMi\agent.log` und `config.log` vorhanden (Config einmal öffnen), Zeilen im
      JSON-Format mit Zeit, Stufe, Modul.
- [ ] In den Logs stehen **keine** Alarmtexte (Profiltext eines ausgelösten Alarms suchen).
- [ ] `C:\ProgramData\HaelpMi\betriebslog.json` mit `{"Stufe": "DEBUG"}` anlegen, HälpMi neu starten
      → mehr Einträge (u. a. Zeitmessungen beim Alarmempfang); Datei wieder löschen, HälpMi neu
      starten → nur noch INFO und höher.
- [ ] Optional, wenn ein Laufwerk `Z:` verbunden ist: Protokolle landen unter
      `Z:\HaelpMi-Logs\<Rechnername>\`. Für den Kundeneinsatz klären, ob das so gewünscht ist.

## 8. Deinstallation

- [ ] HälpMi auf B über „Apps“ deinstallieren → Prozess weg, Autostart weg, Firewall-Regeln
      „HälpMi Agent“ und „HälpMi Konfiguration“ weg.
- [ ] Bei „Einstellungen entfernen“ = Ja: `C:\ProgramData\HaelpMi` ist weg.

## Ergebnis

| Abschnitt | Ergebnis | Bemerkung |
|---|---|---|
| 1 Erstinstallation und Firewall | | |
| 2 Alarmweg | | |
| 3 Kein Empfänger | | |
| 4 Lizenz- und Sperrhinweise | | |
| 5 Selbsttest und Konfiguration | | |
| 6 Update von 0.46.x | | |
| 7 Betriebs-Log | | |
| 8 Deinstallation | | |
