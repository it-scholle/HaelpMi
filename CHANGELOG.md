# Changelog

Alle relevanten Änderungen, für Menschen lesbar. Versionen nach SemVer.
Einträge vor v0.47.4 sind aus den Git-Tags dieser Versionslinie erzeugt (Titel des getaggten Commits);
Details stehen in der Git-Historie. Der eingefrorene Stand `legacy/main-0.39.x` ist hier nicht aufgeführt.

## [Unveröffentlicht]
### Neu
- E2E: drittes Testgerät C (VM 9103) mit den Testfällen T11 (Discovery mit drei Geräten) und T12 (Alarm an zwei Empfänger mit Schwelle 2); Workflow, Relay und Skripte arbeiten mit beliebig vielen Test-VMs aus `ci/e2e/geraete.json`, die Umgebung läuft auf pve-nzxt (#202)
### Verbessert
- Branch-Modell auf `develop`/`main` mit Stabilisierungs-Branch `release-X.Y` umgestellt, Conventional Commits, Hook `pre-push` (#194)
- Struktur an die MasterAgents-Vorlage angeglichen: Doku-Struktur (`docs/`), Arbeitsregeln in `AGENTS.md`, Entscheidungen als ADR, Design-System-Verzeichnis `design/` (#151)
- Einheitliche Linien-Icons (Lucide) statt Schriftzeichen und Einzelzeichnungen; Icon-Buttons haben eine Textalternative für Screenreader (#151)
- Farben und Logo liegen nur noch einmal zentral vor (#151)
- Versionsnummer steht nur noch in `Directory.Build.props`; der Installer liest sie beim Bauen von dort (#168)
### Behoben
- Ein ohne Kundengruppe gebauter Test-Installer bekommt eine zufällige Test-Gruppe statt der Null-GUID und empfängt dadurch Alarme und Boot-Calls; ein Kunden-Installer ohne Kundengruppe lässt sich nicht mehr bauen (#175)
- Eine gültige Lizenz wird nicht mehr durch eine später ausgestellte, aber abgelaufene Lizenz eines anderen Geräts ersetzt (#183)
- Ein im Dashboard deaktiviertes Gerät löst kein Banner „Lizenzkontingent überschritten“ mehr aus; der Sperrhinweis auf dem Gerät nennt den tatsächlichen Grund (#133)
- Der Hinweis „Alarm nicht gesendet“ auf einem gesperrten Gerät erscheint als eigenes Fenster im Vordergrund statt verdeckt unter dem Sperr-Toast (#179)
- Wieder-Aktivieren eines Geräts im Reiter „Geräte / Lizenz“ hebt nur die Deaktivierung auf; das Gerät umgeht das Lizenzkontingent nicht mehr dauerhaft (#180)

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

