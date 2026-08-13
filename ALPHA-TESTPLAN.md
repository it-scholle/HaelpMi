# HälpMi Alpha-Testplan

> **Hinweis 06.08.2026:** Siehe `TEST-STRATEGY.md` im Projekt-Root für die aktuelle
> Gesamt-Teststrategie (coded vs. manuell, Versions-Gates). Ein Teil der Installer-
> Lebenszyklus-Punkte weiter unten (Test 2 teilweise, Test 8, Test 9 teilweise) ist
> inzwischen als coded Suite unter `tests\HaelpMi.Installer.Tests\` vorhanden - dieses
> Dokument bleibt für alles, was (noch) nicht automatisiert ist (UI-Erreichbarkeit, Ton,
> erzwungener Vordergrund). Einige Detailpunkte unten sind veraltet (Version 0.1.1 statt
> aktuell; "Kreise"-Tab existiert seit der 04.08.2026-Klarstellung nicht mehr als
> Dashboard-Objekt, siehe CLAUDE.md) - noch nicht vollständig überarbeitet, bitte beim
> Lesen im Kopf entsprechend übersetzen statt den Fehlern blind zu folgen.

Checkliste für den ersten praktischen Alpha-Test. Alle Pfade sind vollständig ab dem
Projekt-Root angegeben (dem Ordner, der `HälpMi.sln` enthält).

**Bereits von mir verifiziert (Kontext, kein eigener Testschritt):** Payload-Publish,
die einmalige Inno-Setup-Einrichtung und das eigentliche Kompilieren beider Installer-
Skripte (`HaelpMi.iss`, `HaelpMi-Admin.iss`) über direkten ISCC-Aufruf - mit echtem Inno
Setup 7, nicht nur Code gelesen. Dabei ist sogar ein echter Fehler aufgefallen und
behoben worden (`RunOnceId` war an einer Stelle im Skript nicht erlaubt). Details und
Befehle: `HälpMi\BUILD-UND-INSTALLATION.md`.

Was ich damit **nicht** geprüft habe: den tatsächlichen Weg über die
`HaelpMi.InstallCreator`-Oberfläche selbst (Klicks, Häkchen, Protokollanzeige) - nur die
Logik dahinter. Deshalb ist **Test 1** unten der erste echte Testschritt, keine reine
Formalität.

Bitte bei jedem Punkt kurz zurückmelden: **✅ geht** / **❌ geht nicht + was genau
passiert ist** (Fehlermeldung, Screenshot, oder in eigenen Worten was du gesehen hast
statt was erwartet war). Reihenfolge ist absichtlich so gewählt, dass jeder Test auf dem
vorherigen aufbaut.

---

## Test 1: Install-Creator baut den Admin-Installer (Test-Installer angehakt)

Voraussetzung (falls noch nicht geschehen, siehe `HälpMi\BUILD-UND-INSTALLATION.md`):
Inno Setup installiert, kompletter Ordner nach `HälpMi\installer\inno-compiler\` kopiert,
`dotnet publish` für alle drei Payload-Projekte ausgeführt.

- [ ] `HälpMi\tools\InstallCreator\HaelpMi.InstallCreator.exe` per Doppelklick gestartet
      - startet ohne Fehler
- [ ] Häkchen bei "Test-Installer" gesetzt (kein Kundenname/Passwort nötig)
- [ ] "Admin-Installer erstellen" geklickt
- [ ] Protokollfenster zeigt am Ende "Admin-Installer erfolgreich erstellt"
- [ ] Datei liegt unter `HälpMi\installer\Output\HaelpMi-Setup-Admin-0.1.1.exe`

**Wenn das fehlschlägt:** Protokolltext komplett mitschicken (ohne Passwort, das steht da
eh nie drin) - meistens fehlt entweder `HälpMi\installer\inno-compiler\ISCC.exe` oder eine
der drei `payload`-Dateien.

---

## Test 2: Admin-Installer installieren (Rechner/VM #1 - der "Admin-Rechner")

- [ ] `HälpMi\installer\Output\HaelpMi-Setup-Admin-0.1.1.exe` per Doppelklick gestartet,
      UAC-Abfrage bestätigt (braucht Adminrechte, siehe FR-34 - das ist erwartet, nicht
      der App-Betrieb selbst läuft privilegiert)
- [ ] Seite "Raum-Zuordnung" erscheint, Raumbezeichnung + Raumnummer sind Pflicht -
      **absichtlich mal beide leer lassen und auf "Weiter" klicken** → muss eine
      Fehlermeldung zeigen und nicht weiterlassen
- [ ] Beide Felder ausgefüllt, Installation läuft durch
- [ ] Nach Abschluss: `HaelpMi.Agent.exe` läuft automatisch (Task-Manager prüfen)
- [ ] `C:\ProgramData\HaelpMi\settings.json` existiert und enthält Raumname/-nummer,
      `"Role": 1` (Admin)
- [ ] Im Installationsordner (typischerweise `C:\Program Files\HälpMi\`) liegt
      `deployment.json` mit `"Role": 1` und einer Kunden-Gruppen-ID (nicht lauter Nullen)
- [ ] Im Installationsordner liegt `HaelpMi-User-Setup.exe` (fertig vom Install-Creator
      eingebettet - "User-Installer exportieren" kopiert nur noch diese Datei, kompiliert
      nicht mehr selbst)
- [ ] Windows-Dienst "HaelpMiUpdateService" existiert und läuft (`services.msc` oder
      `sc query HaelpMiUpdateService`)
- [ ] Task-Planer: ein "HaelpMi Agent"-Eintrag existiert (Autostart bei Anmeldung)
- [ ] **Regressionstest 11.08.2026** ("Autostart nicht eingerichtet" trotz Admin-Install):
      Task-Planer-Eintrag "HaelpMi Agent" öffnen → Reiter "Allgemein" → Prinzipal muss
      "BUILTIN\Users" heißen (nicht der installierende Admin-Account) → mit einem ZWEITEN,
      NICHT-lokal-administrativen Windows-Konto anmelden (nicht dem, das den Installer
      ausgeführt hat) → HälpMi muss auch dort automatisch starten (Task-Manager/Tray-Icon
      prüfen), keine Tray-Sprechblase "Autostart nicht eingerichtet". Root Cause war: der
      Installer startete den Agent zur Erstregistrierung bewusst de-elevated
      (`runasoriginaluser`), ein Task mit BUILTIN\Users-Prinzipal lässt sich aber nur mit
      Adminrechten anlegen - Fix registriert ihn jetzt vorab noch im elevated
      Installer-Kontext (`--register-autostart`, siehe installer/HaelpMiCommon.iss.inc).

**Melde zurück:** Welcher Punkt bricht ab, welche Datei fehlt/ist leer, welche
Fehlermeldung kam.

---

## Test 3: Konfigurationsprogramm + Admin-Dashboard

- [ ] Start-Menü-Verknüpfung "HälpMi Konfiguration" öffnen
- [ ] Fenster zeigt Computername, Raum, Rolle "Admin" korrekt an
- [ ] Button "Admin-Dashboard öffnen" ist sichtbar (wäre bei einer User-Installation
      nicht da - das aber erst in Test 6 testbar)
- [ ] Dashboard öffnet sich ohne Fehlermeldung zum Edit-Lock (beim allerersten Gerät
      ohne Kreis-Zuordnung sollte das ohne Sperre einfach aufgehen)
- [ ] Tab "Kreise": neuen Kreis anlegen, Namen ändern, Tastenkürzel per Klick ins Feld +
      Tastenkombination setzen, Signalton auswählen, "Speichern" - Statuszeile bestätigt
- [ ] Tab "Gruppen": neue Gruppe anlegen, mehrere Geräte in "Enthaltene Geräte" nacheinander
      *schnell hintereinander* anklicken (Regressionstest v0.8.1 - Race beim Speichern:
      zweiter Klick, bevor der erste fertig verteilt war, hat vorher die ganze Auswahl
      geleert) - alle angeklickten Geräte müssen am Ende grün markiert bleiben, keins darf
      wieder verschwinden
- [ ] Tab "Alarm-Profile": neues Profil anlegen, Text/Tastenkürzel/Schwellwert setzen
- [ ] Im Profil-Tab: "Empfängerkreise verwalten" öffnen, einen Sender hinzufügen, per
      Drag-and-Drop einen Empfänger von "Verfügbar" nach "Zugeordnet" ziehen
- [ ] Dasselbe nochmal nur mit den `>>`/`<<`-Buttons statt Drag-and-Drop (Fallback-Weg)
- [ ] "Rückgängig" auf mindestens einer Änderung ausprobieren - macht sie sichtbar rückgängig
- [ ] Alles nochmal öffnen/schließen - Änderungen sind nach Neustart des Dashboards noch da

**Melde zurück:** Was von alldem hakt, insbesondere Drag-and-Drop (das ist der Teil, den
ich am wenigsten "von Hand" nachvollziehen konnte, weil er reine Maus-/UI-Interaktion ist).

---

## Test 4: User-Installer exportieren (der eigentliche Kernpunkt dieser Änderung)

- [ ] Im Admin-Dashboard oben "User-Installer exportieren" klicken
- [ ] Kurze Wartezeit (Kompilieren im Hintergrund), dann grünes Toast-Fenster unten
      rechts mit Häkchen + "Ordner öffnen"-Button
- [ ] "Ordner öffnen" klicken → Explorer öffnet sich, Datei ist markiert
- [ ] Datei liegt tatsächlich im **echten** Downloads-Ordner (falls der bei dir mal
      verschoben wurde: genau dort, nicht im Standard-Pfad)
- [ ] Toast verschwindet nach ein paar Sekunden von selbst (mit Fade-out)
- [ ] Vorgang wiederholen (zweiter Export) - funktioniert genauso, überschreibt die
      vorherige Datei ohne Nachfrage

**Wenn das fehlschlägt:** Das ist der einzige Teil des Kern-Exports, den ich nicht schon
selbst per Kommandozeile verifiziert habe (die Kompilierung selbst - siehe Test 1 - läuft
nachweislich durch; ungetestet ist nur der Weg über den Button/die Downloads-Ordner-Logik
in der laufenden App). Bitte möglichst genau die Fehlermeldung/den Text im Dialog mitschicken.

---

## Test 5: User-Installer installieren (Rechner/VM #2 - simuliert einen normalen Arbeitsplatz)

- [ ] Die in Test 4 exportierte Datei auf einen ZWEITEN Rechner/eine zweite VM kopieren
      und dort installieren
- [ ] Raum-Zuordnung-Seite erscheint genauso (Raum/Raumnummer Pflicht)
- [ ] `deployment.json` auf Gerät #2 hat **dieselbe** Kunden-Gruppen-ID wie auf Gerät #1
      (Dateien nebeneinander vergleichen)
- [ ] `deployment.json` auf Gerät #2 hat `"Role": 0` (User)
- [ ] Konfigurationsprogramm auf Gerät #2: kein "Admin-Dashboard öffnen"-Button sichtbar
- [ ] Beide Geräte im selben Netzwerk/derselben VM-Netzwerkbrücke - nach spätestens einer
      Minute taucht Gerät #2 in der "Individuell"-Geräteliste auf Gerät #1 auf und
      umgekehrt (Boot-Call-Erkennung)
- [ ] Auf Gerät #1: Kreis-Zuordnung für Gerät #2 im Admin-Dashboard vornehmbar (falls
      noch nicht automatisch geschehen)

**Melde zurück:** Sehen sich die Geräte überhaupt? Das ist der Punkt, an dem sich zeigt,
ob die Kunden-Gruppen-ID wirklich exakt übereinstimmt.

---

## Test 6: Alarm auslösen und empfangen

- [ ] Auf Gerät #1 (oder #2): konfiguriertes Tastenkürzel eines Alarm-Profils drücken
- [ ] Empfangendes Gerät zeigt das Popup **im Vordergrund**, auch wenn gerade ein
      anderes Fenster/Vollbild-Anwendung aktiv war
- [ ] Raum + Raumnummer sind groß/prominent, Benutzername klein
- [ ] Signalton ist hörbar, auch bei stummgeschaltetem Windows-Lautsprecher
- [ ] "Schließen"-Button ist zunächst **deaktiviert**
- [ ] "Bin unterwegs" klicken → Schwellwert-Text im Popup aktualisiert sich, nach
      Erreichen des Schwellwerts wird "Schließen" aktivierbar
- [ ] Auf dem Sender-Gerät: kleines Status-Fenster unten rechts zeigt "Empfangen: x von
      y" und nach dem Klick auch den Namen unter "Auf dem Weg"
- [ ] Popup schließt sich automatisch spätestens 1 Minute nach dem letzten Signal, wenn
      niemand "bin unterwegs" geklickt hat
- [ ] Alarm über die volle Dauer laufen lassen (5 Minuten) ODER 2x "bin unterwegs" von
      zwei verschiedenen Geräten → Sender-Wiederholung stoppt danach automatisch
- [ ] "Abbrechen" auf dem Sender-Status-Fenster beendet die Wiederholung sofort

**Melde zurück:** Vor allem der erzwungene Vordergrund (bei Vollbild-Anwendungen/Spielen)
und der Signalton bei Stummschaltung sind die Punkte, die ich am wenigsten selbst prüfen
konnte.

---

## Test 6b: Testmodus-Toggle (Nutzerwunsch 13.08.2026)

Testet den kompletten echten Alarm-Workflow (Hotkey → P2P-Send → Empfänger-Popup →
Threshold-Gate → "Bin unterwegs" → Sound) gegen ein echtes zweites Gerät, aber eindeutig
als Test gekennzeichnet - nicht zu verwechseln mit dem bestehenden "Selbsttest"-Button
(FR-27, nur Loopback zu sich selbst).

- [ ] Konfigurationsprogramm öffnen, im Bereich "Meine Alarme": Häkchen "Testmodus"
      setzen → Statustext zeigt einen laufenden Countdown ("Testmodus aktiv - noch 1:5x")
- [ ] Konfiguriertes Tastenkürzel eines Alarm-Profils drücken
- [ ] Empfangendes Gerät zeigt das Popup **grün** statt rot, mit diagonalem
      "TEST"-Wasserzeichen und Kopfzeile "HälpMi - TESTALARM" - Raum/Nutzer/Threshold-
      Anzeige unverändert wie bei einem echten Alarm
- [ ] Signalton ist identisch zu einem echten Alarm (keine Abschwächung)
- [ ] Sender-Status-Fenster (unten rechts) zeigt sichtbar "TESTMODUS"
- [ ] "Bin unterwegs" klicken → Threshold-Gate funktioniert wie bei einem echten Alarm
- [ ] Nach dem Trigger: Konfigurationsprogramm zeigt den Toggle automatisch wieder als
      "aus" (Häkchen verschwunden, kein Countdown mehr) - auch nach Neu-Fokussieren des
      Fensters
- [ ] Timeout-Fall: Toggle setzen, **nicht** auslösen, 2 Minuten warten → Toggle springt
      von selbst wieder auf "aus"
- [ ] Negativfall: Toggle setzen, dann manuell wieder ausschalten (Häkchen entfernen) vor
      Ablauf → nächster Hotkey-Trigger kommt beim Empfänger als **normaler** (roter,
      nicht-Test) Alarm an
- [ ] `audit.log` (siehe `Z:\HaelpMi-Logs\`) enthält auf Sender- wie Empfängerseite
      `isTest=true`-Einträge für den Testlauf, `isTest=false` für einen anschließenden
      echten Alarm

**Melde zurück:** Vor allem, ob der Toggle-Status im Konfigurator jederzeit korrekt
anzeigt (auch nach Fenster-Minimieren/Wiederherstellen) - das ist die Absicherung gegen
einen versehentlich scharf bleibenden Testmodus.

---

## Test 7: Konfigurations-Synchronisation (Hot-Reload)

- [ ] Auf Gerät #1 im Dashboard einen bestehenden Kreis umbenennen
- [ ] Auf Gerät #2 (ohne Neustart der App!): Innerhalb kurzer Zeit spiegelt sich die
      Änderung wider (z. B. im lokalen Cache/Verhalten sichtbar)
- [ ] Ein zweites Admin-Gerät gleichzeitig öffnen (falls verfügbar) und versuchen, im
      selben Kreis gleichzeitig zu editieren → eines der beiden sollte eine "wird gerade
      bearbeitet"-Meldung bekommen, nicht beide gleichzeitig unbemerkt schreiben

---

## Test 8: Deinstallation (beide Installer-Typen)

- [ ] Admin-Installation über Windows "Apps & Features" deinstallieren
- [ ] Rückfrage nach "auch lokale Einstellungen/Geräteliste entfernen?" erscheint
- [ ] Nach Deinstallation: `HaelpMi.Agent.exe`-Prozess ist weg, Task-Planer-Eintrag weg,
      Windows-Dienst "HaelpMiUpdateService" weg (`sc query HaelpMiUpdateService` → Fehler
      "der angegebene Dienst ist nicht installiert")
- [ ] Bei "Ja" auf die Rückfrage: `C:\ProgramData\HaelpMi` ist komplett weg
- [ ] Dasselbe für die User-Installation auf Gerät #2 wiederholen

---

## Test 9: Update-Pipeline — ⚠️ mit Vorsicht, das ist der riskanteste Teil

**Nur auf Wegwerf-VMs testen, nie auf einem Gerät mit wichtigen Daten.** Das ist
selbstmodifizierende Software mit einem Windows-Dienst mit Systemrechten - genau der
Teil, den ich am wenigsten selbst verifizieren konnte.

- [ ] Zwei Geräte auf unterschiedlichen (künstlich erzeugten) Versionsständen bringen
      (z. B. eine mit geänderter `MyAppVersion` in
      `HälpMi\installer\HaelpMiCommon.iss.inc` neu gebaute Test-Installation)
- [ ] Beobachten, ob das ältere Gerät die neuere Version überhaupt bemerkt (Log/Audit)
- [ ] **Falls es zu einem Absturz, Hänger oder halb-installierten Zustand kommt: nicht
      selbst reparieren versuchen** - Zustand (Prozesse, Dateien unter `{app}` und
      `{app}\versions\`, Dienst-Status) so gut wie möglich notieren/Screenshot und mir
      zurückmelden, das ist genau die Art Fehler, die ich vorab nicht sehen konnte.

---

## Was mir beim Zurückmelden am meisten hilft

1. Welcher Test/Punkt genau (z. B. "Test 4, dritter Punkt")
2. Erwartet vs. tatsächlich passiert, in eigenen Worten reicht
3. Bei Fehlermeldungen: möglichst den kompletten Text (Copy-Paste, kein Passwort/keine
   echten Kundendaten dabei — die sollten ohnehin nirgends im Klartext auftauchen)
4. Bei Abstürzen: `%LocalAppData%\HaelpMi\crash-*.log` (CrashLogger) falls vorhanden

Alles, was hier NICHT als eigener Punkt steht, aber trotzdem komisch aussieht, bitte
genauso melden - diese Liste deckt ab, was ich mir vorstellen konnte zu testen, nicht
zwangsläufig alles, was in der Praxis auffällt.
