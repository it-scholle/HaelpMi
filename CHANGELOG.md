# Changelog

Alle relevanten Änderungen, für Menschen lesbar. Versionen nach SemVer.
Einträge vor v0.47.4 sind aus den Git-Tags dieser Versionslinie erzeugt (Titel des getaggten Commits);
Details stehen in der Git-Historie. Ab 1.0.0 nennt jeder Abschnitt nur Änderungen, die Anwender und Administratoren betreffen. Der eingefrorene Stand `legacy/main-0.39.x` ist hier nicht aufgeführt.

## [1.0.0] – unveröffentlicht
Erste freigegebene Version. Aufgeführt ist, was sich gegenüber den Vorversionen 0.46.x für Anwender und Administratoren ändert. Änderungen an Entwicklungswerkzeugen, automatischen Tests und Arbeitsabläufen stehen nur in der Git-Historie.
### Neu
- Betriebsprotokoll zur Fehlersuche: `agent.log` (Hintergrundprogramm) und `config.log` (Einstellungen und Dashboard) unter `%ProgramData%\HaelpMi`, ohne Alarmtexte oder andere Inhalte. Eine Datei pro Tag, ältere werden komprimiert und nach 90 Tagen gelöscht, höchstens 200 MB je Programm. Ein ausführlicheres Protokoll lässt sich ohne Update über `betriebslog.json` einschalten (#227)
- Empfängt ein Gerät Nachrichten einer anderen HälpMi-Installation im selben Netz (z. B. ein mit falschem Installer eingerichtetes Gerät), steht das als Warnung im Betriebsprotokoll, höchstens einmal pro Stunde je Art und fremder Installation. Die Nachrichten werden wie bisher ignoriert (#214)
- Die Installer richten die Windows-Firewall-Freigaben für HälpMi selbst ein; Windows fragt beim ersten Start nicht mehr nach (#150)
- Ist für ein Alarm-Profil kein Empfänger eingerichtet oder sind alle Empfänger deaktiviert, zeigt das Sender-Gerät sofort „Kein Empfänger eingestellt!“ mit dem Hinweis, dass ein Admin im Dashboard Empfänger einrichten muss; die Meldung blendet sich nach wenigen Sekunden aus. Nach 30 Sekunden erscheint zusätzlich eine neutrale Windows-Benachrichtigung „Wartung erforderlich“ mit der Bitte, die IT zu informieren, höchstens eine pro 30 Sekunden. Absender ist „Arbeitsplatz-Dienst“, ein kleines neues Hilfsprogramm (`HaelpMi.Notify.exe`) im Programmordner, damit die Benachrichtigung den Alarm nicht verrät (#232)
### Verbessert
- HälpMi läuft auf .NET 10 (Langzeit-Support bis November 2028) statt .NET 8; die Laufzeit ist im Installer enthalten, eine eigene .NET-Installation ist nicht nötig (#154)
- Einheitliche Linien-Symbole; Symbol-Schaltflächen haben eine Textalternative für Screenreader (#151)
- Die Versionsnummer im Installer und im Programm stimmt immer überein (#168)
- Ein Gerät, das in einem Alarm-Profil nicht als Sender eingetragen ist, reagiert auf dessen Tastenkürzel nicht mehr und zeigt keine Meldung (#232)
### Behoben
- Das Alarmfenster ließ sich in seltenen Fällen nicht schließen oder öffnete sich direkt nach dem Schließen noch einmal. Nach Erreichen der Schwelle, nach einem Abbruch durch den Sender und nach Ablauf lässt es sich jetzt zuverlässig schließen; spätere Wiederholungen desselben Alarms öffnen kein neues Fenster (#136)
- Bricht der Sender einen Alarm ab, wird das an alle Empfänger übertragen, und deren Fenster lassen sich sofort schließen (#36)
- Der Sender zeigt „empfangen“ erst, wenn der Alarm auf dem Empfänger-Gerät wirklich angezeigt wird, und nicht schon, während dort zum Beispiel noch ein Benutzerwechsel läuft (#37)
- Die Anzeige „Empfangen: x von y“ beim Sender zählt alle Empfänge eines Alarms, auch die aus späteren Wiederholungen (#182)
- Nach dem ersten empfangenen Alarm wiederholt der Sender seine Alarm-Meldungen wieder im vorgesehenen Takt von wenigen Sekunden statt erst nach rund 30 Sekunden (#177)
- Ein Gerät, das während eines laufenden Alarms startet oder seine Netzwerkadresse wechselt, wird von den folgenden Wiederholungen erreicht; der Alarm-Empfang läuft schon, bevor sich das Gerät im Netz meldet (#73)
- Hat ein Gerät in seinem Alarm-Profil keine Empfänger, bleibt das Tastenkürzel nicht mehr wirkungslos: Das Gerät zeigt einen Hinweis, wo ein Admin die Empfänger im Dashboard einrichtet (#41)
- Nach einer Installation, Reparatur oder einem Update empfängt ein Gerät Alarme zuverlässig: Die Firewall-Freigaben werden angelegt, bevor HälpMi zum ersten Mal startet; Sperren aus einer früher abgebrochenen Windows-Firewall-Abfrage werden entfernt (#222)
- Nach einer Erstinstallation startet HälpMi sofort vollständig; vorher konnte direkt nach der Installation die Meldung „Installation unvollständig“ erscheinen (#229)
- Der Selbsttest im Einstellungsfenster meldet, wenn er nicht möglich ist oder der Testalarm nicht ankommt, mit Grund und Lösungsweg (z. B. fehlendes Alarm-Profil) und bietet bei Bedarf das Admin-Dashboard an (#176)
- Lizenzablauf und „läuft bald ab“ werden auch im laufenden Betrieb erkannt, nicht erst nach einem Neustart; User-Geräte zeigen bei abgelaufener Lizenz einen Hinweis, Alarme laufen weiter (#181)
- Eine gültige Lizenz wird nicht mehr durch eine später ausgestellte, aber abgelaufene Lizenz eines anderen Geräts ersetzt (#183)
- Ein im Dashboard deaktiviertes Gerät löst kein Banner „Lizenzkontingent überschritten“ mehr aus; der Hinweis auf dem gesperrten Gerät nennt den tatsächlichen Grund (#133)
- Der Hinweis „Alarm nicht gesendet“ auf einem gesperrten Gerät erscheint als eigenes Fenster im Vordergrund statt verdeckt unter einer anderen Meldung (#179)
- Wieder-Aktivieren eines Geräts im Reiter „Geräte / Lizenz“ hebt nur die Deaktivierung auf; das Gerät umgeht das Lizenzkontingent nicht mehr dauerhaft (#180)
  - Hinweis für bestehende Installationen: Geräte, die vorher wieder aktiviert wurden, umgehen das Kontingent weiterhin. Es gibt keine automatische Umstellung; ein Admin schaltet solche Geräte im Reiter „Geräte / Lizenz“ einmal aus und wieder ein.
- Das Alarmfenster ist auf dem Empfänger sofort bedienbar, auch beim ersten Alarm nach dem Hochfahren, und der Sender sieht „empfangen“ nach etwa einer Sekunde. Vorher konnte das Fenster bis zu 24 Sekunden lang nicht reagieren, und die Empfangsbestätigung kam so spät, weil sie auf den Start des Alarmtons wartete (#238)

## [0.47.3] – 2026-11-18
- Einrichtungs-Skript für die E2E-Vorlagen-VM (#165)

## [0.47.1] – 2026-11-18
- CI (Build + Tests auf Windows-Runner), gitleaks und Dependabot (#145)

## [0.47.0] – 2026-10-01
- Terminal-State-Tracking + Relay-Burst gegen hängenbleibendes Alarmfenster (#136)

## [0.46.1] – 2026-09-23
- (Merge-Commit, Details siehe Git-Historie)

## [0.46.0] – 2026-09-22
- Inaktive Update-Pipeline als DEPRECATED für release-1.0-MVP markiert

## [0.44.2] – 2026-09-21
- Admin-Selbstdeaktivierung zeigt keinen Lizenzlimit-Toast mehr

## [0.44.0] – 2026-09-19
- Admin kann sich im Geräte-Tab selbst deaktivieren (Dashboard bleibt nutzbar)

## [0.43.0] – 2026-09-10
- Endgueltig geloeschtes Geraet lebt nicht mehr per Gossip bei anderen Peers wieder auf

## [0.42.1] – 2026-09-09
- Versionsnummer-Kollision mit v0.42.0 (Issue #99) aufgeloest

## [0.41.0] – 2026-09-08
- Deinstallation meldet sich automatisch und sichtbar statt versteckt zu deaktivieren

## [0.40.1] – 2026-09-08
- Geraet-Loeschung erreicht das Endgeraet jetzt per direktem Unicast, nicht nur Broadcast

## [0.40.0] – 2026-09-08
- Geraet loeschen deaktiviert es jetzt wirklich statt nur die Uebersicht

## [0.39.2] – 2026-09-08
- Alarm-Ack/Status-Relay haengt nicht mehr fest, wenn Empfaenger gerade erst startet (Logoff/Logon)

## [0.39.1] – 2026-09-08
- Zwischenablage-Kopieren blockiert das UI gar nicht mehr statt nur kuerzer

## [0.39.0] – 2026-09-08
- Zwischenablage-Kopieren haengt nicht mehr ~20s, Klick ins Lizenzschluessel-Feld kopiert direkt

## [0.38.3] – 2026-09-08
- Reiter im Admin-Dashboard bleiben ohne Lizenz anklickbar (Issue #84, Nacharbeit zu #77)

## [0.38.2] – 2026-09-08
- Geräte-Tab-Toggles flackern nicht mehr bei jedem Refresh (Issue #61 Nacharbeit)

## [0.38.1] – 2026-09-08
- Geräte-Tab verhindert Aktivieren über das Lizenzkontingent hinaus (Issue #61 Nacharbeit)

## [0.36.0] – 2026-09-07
- Admin-Dashboard-Bearbeitung ohne gültige Lizenz gesperrt (Issue #77)

## [0.35.0] – 2026-09-07
- Admin-Dashboard nur beim installierenden Windows-Nutzer (Issue #10)

## [0.34.2] – 2026-09-07
- (Merge-Commit, Details siehe Git-Historie)

## [0.34.1] – 2026-09-07
- (Merge-Commit, Details siehe Git-Historie)

## [0.34.0] – 2026-09-07
- (Merge-Commit, Details siehe Git-Historie)

## [0.33.2] – 2026-09-07
- (Merge-Commit, Details siehe Git-Historie)

## [0.33.1] – 2026-09-07
- (Merge-Commit, Details siehe Git-Historie)

## [0.33.0] – 2026-09-07
- (Merge-Commit, Details siehe Git-Historie)

## [0.32.1] – 2026-09-07
- Moderne Scrollbar-Optik im Dashboard (Issue #12)

## [0.32.0] – 2026-09-07
- Custom-Lizenzpaket mit frei wählbarer Geräteanzahl (Testlizenzen)

## [0.31.2] – 2026-09-03
- Systemstart-Erinnerung verschwindet jetzt nach erfolgreichem Lizenz-Einspielen

## [0.31.1] – 2026-09-03
- Anbieter-Kontaktdaten in Lizenzwarnungen (Issue #20 vervollständigt)

## [0.31.0] – 2026-09-03
- Systemstart-Erinnerung vor Lizenzablauf (Issue #20-Nacharbeit)

## [0.30.2] – 2026-09-02
- Lizenzdatei-Pfad ins beschreibbare ProgramData verschoben (Issue #57)

## [0.30.1] – 2026-09-02
- Nach-Z:-verschieben freezed Install-Creator nicht mehr (async File.Move)

## [0.30.0] – 2026-09-02
- Lizenzschlüssel pro Kundengruppe statt globalem Schlüsselpaar (Issue #56)

## [0.29.2] – 2026-09-02
- Lizenz einspielen zeigt jetzt eine Rückmeldung statt nichts zu tun (Issue #55)

## [0.26.0] – 2026-09-01
- Lizenz-Ablaufwarnung im Admin-Dashboard + Lizenz-Import (Issue #20, #51)

## [0.25.2] – 2026-09-01
- XS/Trial statt Trial im Lizenz-Tier-Picker (Nacharbeit #46)

## [0.24.0] – 2026-08-27
- LicenseTierPicker-Control (S/M/L/XL/Trial-Nutzerlimit, Issue #46)

## [0.23.0] – 2026-08-27
- Lizenz einlesen und prüfen - CustomerGroupId-Bindung, Soft-Expiry (Issue #19)

## [0.22.3] – 2026-08-27
- LizenzAblauf aus Kundenregister entfernt - redundant mit Lizenzregister (#21)

## [0.22.2] – 2026-08-27
- Update-Reiter im Admin-Dashboard versteckt (Issue #26)

## [0.22.1] – 2026-08-27
- Testnummer-Vorschlag zeigt T-Praefix (Nacharbeit #43)

## [0.22.0] – 2026-08-26
- Lizenzregister-Datenstruktur - Lizenz an CustomerGroupId gebunden (Vorarbeit #18/#19)

## [0.21.0] – 2026-08-26
- Testinstaller bekommen eigenes Nummernregister T0001+ (Issue #43)

## [0.20.0] – 2026-08-26
- Lokales Kundenregister ersetzt Zähler aus #39 (Issues #31, #21)

## [0.19.1] – 2026-08-26
- Feedback-Relay liefert Rückstand an neu verbundene Satellites nach (Issue #38)

## [0.19.0] – 2026-08-26
- "Über mich"-Popup im Tray-Menü (Issue #6)

## [0.18.0] – 2026-08-26
- Kundennummer-Vergabe ab 10001 + Zuordnung zur internen UUID (Issue #39)

## [0.17.6] – 2026-08-26
- Admin-Gerät registriert eigene Alarm-Hotkeys sofort neu (Fixes #35)

## [0.17.5] – 2026-08-26
- Sender-Statuspopup wächst bei neuen Antworten nach oben statt in die Taskleiste (Issue #7)

## [0.17.4] – 2026-08-26
- Alarm-Feedback-Kanal Primary/Satellite bei Fast User Switching (Fixes #34)

## [0.17.3] – 2026-08-26
- Testplan-Nachtrag - Popup-Unterdrückung in nicht sichtbarer Sitzung (Issue #9-Nachtrag)

## [0.17.2] – 2026-08-26
- Alarmpopup nur in aktiv sichtbarer Sitzung anzeigen (Issue #9-Nachtrag)

## [0.17.1] – 2026-08-25
- Alarmempfang je Sitzung bei Fast User Switching (Issue #9)

## [0.17.0] – 2026-08-25
- Nach-Z:-verschieben-Button neben Anzeigen im Admin-Installer-Erfolgsbanner

## [0.16.16] – 2026-08-25
- Mouse.Capture am ComboBox-Popup entfernt - blockierte Aussenklick-Schliessen (#28)

## [0.16.15] – 2026-08-25
- Schwarzer Balken zwischen ComboBox und Liste behoben (#29)

## [0.16.14] – 2026-08-25
- Eigene Hover-Vorschau per Cursor-Polling statt IsHighlighted (#27)

## [0.16.13] – 2026-08-25
- Window_PreviewMouseDown reisst ComboBox waehrend des Oeffnens den Fokus weg (#1)

## [0.16.12] – 2026-08-25
- SoftwareOnly-Rendering gegen ausbleibende Hover-Vorschau im Popup (#1)

## [0.16.11] – 2026-08-25
- PART_Popup-Name + expliziter Mouse.Capture gegen ComboBox-Klick-Bug (#1)

## [0.16.10] – 2026-08-25
- WS_EX_NOACTIVATE gegen ComboBox-Popup-Klick unter RDP/VM (#1)

## [0.16.9] – 2026-08-25
- Install-Creator leert payload/ und UserInstallerPayload/ vor jedem Build (Fixes #25)

## [0.16.8] – 2026-08-24
- Sender bei eigenem Alarm nie mehr eigener Empfänger (Fixes #5)

## [0.16.7] – 2026-08-24
- Kommentarregel verschärft, ComboBox-Popup-Kommentar entsprechend gekürzt

## [0.16.6] – 2026-08-24
- ComboBox-Popup-Klick-Regression behoben (Fixes #1)

## [0.16.5] – 2026-08-19
- Update-Pipeline komplett ignoriert, nur noch de-Sprachressourcen

## [0.16.4] – 2026-08-19
- Standard-Kundenname im Install-Creator ist jetzt TG<Version>

## [0.16.3] – 2026-08-19
- Fliegender Configaustausch - normaler Announce bei allen drei Config-Ereignissen

## [0.16.2] – 2026-08-19
- Install-Creator UI aufgeraeumt (Version links unten, Klammertexte, Passwort-Infotext entfernt)

## [0.16.1] – 2026-08-19
- Selbsttest-Audio in der Testsuite stummgeschaltet

## [0.16.0] – 2026-08-11
- Boot-Call zieht fehlende Config nach - frisch installierte/lange offline gewesene Geräte bleiben nicht mehr ohne Konfiguration

