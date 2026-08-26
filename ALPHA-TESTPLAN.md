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
- [ ] Direkt danach, OHNE Neustart von Agent/Dashboard: das neue Tastenkürzel auf genau
      diesem (Admin-)Gerät drücken - muss sofort senden (Regressionstest 26.08.2026:
      `HandleRebroadcastRequestAsync` registrierte Hotkeys beim eigenen Admin-Gerät bisher
      nicht neu, andere Geräte bekamen die Änderung sofort, das Admin-Gerät selbst erst
      nach einem Neustart des Agent-Prozesses)
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

## Test 5b: "HälpMi starten" (Nutzerwunsch 18.08.2026, manueller Neustart nach Absturz)

Automatisiert deckt `HaelpMi.Installer.Tests` schon ab, dass die Verknüpfung existiert und
auf die richtige exe zeigt sowie dass kein Zweitprozess entsteht - hier geht es nur um das,
was sich nicht sinnvoll automatisieren lässt: die tatsächlich sichtbare Tray-Sprechblase.

- [ ] Startmenü (Gerät #1 **und** #2, also Admin- **und** User-Installer): Eintrag
      "HälpMi starten" ist vorhanden
- [ ] `HaelpMi.Agent.exe` per Taskmanager beenden, dann "HälpMi starten" klicken →
      Tray-Icon erscheint wieder, kein Fehlerdialog
- [ ] Bei bereits laufendem Agent erneut "HälpMi starten" klicken → Tray-Sprechblase
      "HälpMi läuft bereits" erscheint, im Taskmanager bleibt weiterhin **genau ein**
      `HaelpMi.Agent.exe`-Prozess sichtbar (kein zweites Fenster, kein Doppel-Icon)

**Melde zurück:** Erscheint die Sprechblase zuverlässig, oder nur manchmal (z. B. weil der
Agent noch mitten im Hochfahren war)? Das ist der einzige Teil, den ich als Diagnose-Kommentar
im Code schon als seltene Startrace eingeplant habe, aber nicht selbst timen konnte.

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

### Test 9a: Flaw 26 - Prozessersatz nach dem Swap tatsächlich verifizieren

Regressionstest für den Fehlerbericht "'HälpMi läuft bereits' nach einem Update, alte
Version lief unbemerkt mit altem Code weiter, Neustart/'erneut suchen' änderten nichts"
(v0.39.2). Deckt genau das ab, was Test 9 oben bisher nicht geprüft hat: nicht nur "hat
der Swap irgendetwas gemeldet", sondern "läuft am Ende wirklich der neue Code, nicht nur
liegen neue Dateien auf der Platte".

- [ ] Update wie in Test 9 auslösen, dabei den Taskmanager offen halten und die PID der
      `HaelpMi.Agent.exe` **vor** dem Update notieren
- [ ] Direkt nach dem gemeldeten Update-Erfolg per Taskmanager ("Details"-Tab, Pfad-Spalte
      einblenden) prüfen: läuft jetzt eine **neue** PID, deren Pfad ins normale
      Installationsverzeichnis zeigt (nicht mehr unter `{app}\versions\...`)? Zu keinem
      Zeitpunkt dürfen zwei `HaelpMi.Agent.exe`-Prozesse gleichzeitig sichtbar sein
      (kurz danach pollen, z. B. `Get-Process HaelpMi.Agent` alle 200 ms für 10s)
- [ ] Im Admin-Dashboard NICHT nur die eigene Versionsanzeige ansehen (die zeigt immer nur
      sich selbst, siehe Root-Cause-Notiz oben) - stattdessen die tatsächlich laufende
      Version direkt beim Agent-Prozess verifizieren (Taskmanager-Dateiversion des
      `HaelpMi.Agent.exe` unter der neuen PID)
- [ ] **Absichtlicher Fehlschlagstest:** vor dem Update die alte `HaelpMi.Agent.exe` per
      Taskmanager "Task beenden" hart abschießen, während sie gerade eine Datei geöffnet
      hält (z. B. Log-Datei-Handle offen lassen, falls einfach herstellbar) - erwartet:
      entweder sauberer Swap-Erfolg mit neuer PID, oder ein sauberer, protokollierter
      Rollback (alte Version läuft danach unverändert weiter, kein Gerät ganz ohne Agent)
      - nie ein stiller "Erfolg" bei tatsächlich weiterlaufender Altversion
- [ ] Danach Gerät neu starten - Version muss sich gegenüber dem Zustand direkt nach dem
      Update NICHT mehr ändern (war sie vorher schon korrekt aktualisiert, bleibt sie es)

## Test 9b: Update-Ei / Vaultwarden (Nutzerwunsch 13.08.2026)

Deckt den in `HaelpMi.InstallCreator` neu hinzugekommenen Weg ab, ein Update-Paket zu
signieren - hier bewusst nicht automatisiert (echter Vaultwarden-Zugriff nötig).

- [ ] Install-Creator öffnen, Vaultwarden-Karte mit Server-URL/Konto-E-Mail/Master-Passwort
      ausfüllen, "Schlüssel laden" - Statuszeile zeigt Erfolg, "Update erstellen" wird
      aktivierbar
- [ ] "Neuen Schlüssel erzeugen" (nur auf einer Wegwerf-/Test-Vaultwarden-Instanz, nicht auf
      der echten Produktiv-Notiz!) - öffentlicher Schlüssel erscheint im Protokoll und in der
      Zwischenablage; in Vaultwarden selbst prüfen, ob die Notiz `HälpMi-Update-PrivateKey`
      tatsächlich angelegt wurde
      - [ ] Erneut klicken, während schon ein Schlüssel existiert - Warndialog vor dem
            Überschreiben muss erscheinen, "Nein" darf nichts verändern
- [ ] Test-Installer bauen wie in Test 1 (seit 16.08.2026 kein "Update-Ei"-Häkchen mehr -
      passiert automatisch, sobald ein Schlüssel geladen ist) - danach prüfen, dass
      `installer/payload/update-seed/manifest.json` und `package.zip` frisch entstanden sind
- [ ] Diesen Test-Installer installieren (wie Test 2) - im Admin-Dashboard, Tab "Updates",
      zeigt die Versions-Combobox jetzt die gebaute Version an (siehe Fehlerbericht
      13.08.2026, "Dropdown bleibt leer")
- [ ] Ohne geladenen Schlüssel bauen (Vaultwarden-Karte übersprungen) - Test-Installer muss
      weiterhin klaglos bauen, nur ohne eingebettetes Startpaket (Protokoll sagt das explizit)

## Test 9d: Self-Bootstrap-Update (Nutzerwunsch 16.08.2026, "Update erstellen") — ⚠️ ebenfalls
mit Vorsicht, gleiche Kategorie wie Test 9 (selbstmodifizierend, Dienst mit Systemrechten)

Deckt genau den Teil ab, der von den coded Tests nicht erreichbar ist: das tatsächliche
Anhängen/Auslesen des Pakets an eine echte veröffentlichte .exe, und der komplette
Install-StartTest-ConfirmSwap-Ablauf gegen einen echten laufenden `HaelpMi.UpdateService`,
ausgelöst OHNE vorherige Peer-Beobachtung.

- [ ] Auf der Build-Maschine: Update-Schlüssel geladen (siehe Test 9b), "Update erstellen"
      klicken - Protokoll zeigt Payload-Publish, Paket-Signatur, `dotnet publish` des
      Bootstrap-Werkzeugs, dann "Update erstellt: ...\Output\HaelpMi-Update-<Version>.exe"
- [ ] Diese eine Datei auf ein zweites, bereits installiertes Testgerät (Wegwerf-VM!)
      kopieren und dort per Doppelklick ausführen - Konsolenausgabe sollte Schritt für
      Schritt Signaturprüfung/Installation/Selbsttest/Übernahme zeigen
- [ ] Nach "Fertig" auf diesem Gerät prüfen: läuft `HaelpMi.Agent.exe` jetzt tatsächlich mit
      der neuen Versionsnummer (Tray-Icon-Tooltip/Dashboard "Eigene Version")?
- [ ] Im Admin-Dashboard, Tab "Updates": erscheint die Version jetzt in der Auswahl-Liste,
      ohne dass dieses Gerät sie je von einem Peer gezogen hätte?
- [ ] Freigeben klicken, ein drittes (älteres) Testgerät im selben Netz beobachten - zieht es
      die Version automatisch (Wellen-Rollout, siehe CLAUDE.md), sobald es einen Boot-Call
      von der bereits aktualisierten Maschine hört?
- [ ] Die rohe, unbestückte `HaelpMi.UpdateBootstrapper.exe` (ohne über "Update erstellen"
      gelaufen zu sein, z. B. direkt aus `installer/UpdateBootstrapperPublish/`) separat
      ausführen - muss eine klare Fehlermeldung zeigen ("kein eingebettetes Update-Paket"),
      nicht abstürzen
- [ ] **Falls es zu einem Absturz, Hänger oder halb-installierten Zustand kommt: nicht selbst
      reparieren versuchen** - gleiches Vorgehen wie bei Test 9 (Zustand notieren/Screenshot,
      zurückmelden)

## Test 9c: Revisionssicheres Audit-Log — Push-Sync an Admin-Geräte (Nutzerwunsch 14./15.08.2026)

Deckt die Verteilung ab, die coded Tests (Loopback, ein Prozess) nicht zeigen können: echte
zwei Geräte, echte Netzwerktrennung.

- [ ] Ein Admin-Gerät und ein User-Gerät derselben Kunden-Gruppe installiert und beide
      einmal gebootet (Discovery-Kontakt hergestellt, siehe Test 2-4)
- [ ] Auf dem User-Gerät einen Alarm auslösen und vollständig auslaufen lassen (Zeitablauf
      oder Schwellwert, inkl. der 1-Minute-Nachlauf danach)
- [ ] Auf dem Admin-Gerät prüfen: unter
      `C:\ProgramData\HaelpMi\audit-ingest\{DeviceId-des-User-Geräts}.jsonl` sind die
      Einträge zu diesem Alarm angekommen (Seq beginnt bei 1, PrevHash/EntryHash sehen wie
      Hex-Strings aus, keine `*.gaps.jsonl`-Datei danaben)
- [ ] Admin-Gerät kurz vom Netz trennen (WLAN aus/Kabel ziehen), auf dem User-Gerät einen
      weiteren Alarm auslösen und auslaufen lassen, Admin-Gerät wieder verbinden, einmal neu
      booten (oder "Erneut suchen" im Tray) - die zwischenzeitlich entstandenen Einträge
      müssen nachträglich in derselben `.jsonl`-Datei auftauchen, fortlaufend an die
      vorherige Seq anschließend
- [ ] Falls ein zweites Admin-Gerät verfügbar ist: beide Admin-Geräte einmal gleichzeitig
      booten (oder eines neu starten, während das andere schon läuft) - beide sollten
      danach dieselben `audit-ingest\*.jsonl`-Dateien mit demselben Stand haben
      (Admin↔Admin-Mesh-Abgleich)

**Wenn das fehlschlägt:** welcher Schritt genau, Inhalt der betroffenen `.jsonl`-Datei
(ohne Klarnamen sollte da ohnehin nichts drinstehen) sowie ob eine `*.gaps.jsonl`-Datei
entstanden ist, mitschicken.

## Test 9e: Fast User Switching — Autostart + Alarmempfang für jede angemeldete Sitzung (17.08.2026)

Kernanforderung, kein Komfort-Test: jeder angemeldete Windows-Account muss zuverlässig Alarme
empfangen, auch bei Benutzerwechsel ohne Ab-/Anmeldung. Braucht ein Testgerät mit zwei lokalen
Windows-Standard-Accounts (A und B), HälpMi darauf installiert.

- [ ] Account A anmelden - Agent läuft (Tray-Icon sichtbar), Alarm von einem Peer-Gerät
      auslösen - A bekommt Popup+Ton (Baseline, unverändert)
- [ ] Ohne A abzumelden: "Benutzer wechseln" (Startmenü) → Account B anmelden. Tray-Icon für
      B muss binnen weniger Sekunden erscheinen (Task-Scheduler-LogonTrigger, jetzt mit
      `Parallel`-Policy statt `IgnoreNew`)
- [ ] Erneut Alarm von einem Peer-Gerät auslösen - **B muss Popup+Ton bekommen**, obwohl B den
      Alarm-TCP-Port nicht selbst hält (Satellite-Relay von A's Primary-Instanz, siehe
      AlarmRelayServer/AlarmRelayClient)
- [ ] Zurück zu Account A wechseln, erneut Alarm auslösen - A weiterhin funktionsfähig
      (Primary unverändert aktiv)
- [ ] Account A **komplett abmelden** (nicht nur wegschalten), während B aktiv ist - B muss
      binnen kurzer Zeit selbst zur Primary werden (Relay-Pipe-Abbruch löst Bind-Übernahme
      aus); mit einem weiteren Peer-Alarm nach der Übernahme verifizieren
- [ ] In Sitzung B `HaelpMi.Config` öffnen, "Erneut suchen" oder Selbsttest auslösen - muss
      gegen B's eigene Agent-Instanz laufen (jeweiliges lokales Audit-Log prüfen), nicht
      gegen die von A (Test für die Session-Scoping-Korrektur der Config↔Agent-IPC-Pipe)
- [ ] Stresstest: mehrfach schnell zwischen A und B hin- und herwechseln - keine hängenden
      Prozesse, kein Deadlock bei der Primary/Satellite-Übernahme
- [ ] **Nachtrag 25.08.2026 (nicht sichtbare Sitzung darf kein Popup zeigen):** zu Account B
      wechseln, dann zurück zu A wechseln (B bleibt angemeldet, aber im Hintergrund/
      `WTSDisconnected`). Alarm auslösen, während NUR A sichtbar ist - **B darf KEIN neues
      Popup zeigen** (weder bei sich selbst sichtbar noch nach späterem Zurückwechseln zu B,
      solange keine weitere Signal-Wiederholung während B's Aktivphase eintrifft), da niemand
      es in B's unsichtbarer Sitzung hätte wegklicken können (`ActiveSessionDetector`). A
      bekommt Popup+Ton wie gewohnt. Danach zu B zurückwechseln und prüfen, dass B ganz normal
      auf den NÄCHSTEN Alarm reagiert (Suppression ist pro Signal, keine dauerhafte Sperre).

**Wenn das fehlschlägt:** welcher Punkt genau, ob das Tray-Icon für die zweite Sitzung
überhaupt erscheint (Autostart-Problem) oder nur der Alarm ausbleibt (Relay-Problem), plus
`%LocalAppData%\HaelpMi\crash-*.log` beider Sitzungen falls vorhanden.

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
