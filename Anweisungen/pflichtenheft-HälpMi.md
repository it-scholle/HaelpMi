# Pflichtenheft: LAN-Alarmierungssystem für Verwaltungsnetzwerk

Arbeitstitel / Programmname: **HälpMi**

**Version 2, Stand 02.08.2026.** Diese Version ersetzt Version 1 nicht, sondern erweitert sie um
Abschnitt 10 (Admin-Rollen, Konfigurationsverwaltung, Auto-Update) nach einem Rücksprachegespräch.
Einzelne Anforderungen aus Version 1 sind dadurch inhaltlich überholt – sie werden **nicht
gelöscht**, sondern an Ort und Stelle als "ersetzt durch FR-XX" markiert, damit die
Entscheidungshistorie nachvollziehbar bleibt. Die zugehörigen technischen Umsetzungsdokumente für
Claude Code sind `CLAUDE.md` (dauerhafte Architekturregeln) und
`claude-code-prompt-teil2-admin-update.md` (konkreter Umsetzungs-Prompt).

## 1. Kontext & Grundprinzip

Internes Verwaltungsnetzwerk einer Ordnungsbehörde, ausschließlich Windows-Endgeräte. Ziel: Per Tastenkürzel wird auf allen dafür freigegebenen Geräten ein erzwungenes Popup mit fester Nachricht angezeigt und ein Signalton abgespielt – auch bei Stummschaltung oder Kopfhörer.

**Grundprinzip (Version 1, seit Abschnitt 10 überholt): kein Admin, keine zentrale Instanz.**
Ursprünglich war jeder Arbeitsplatz gleichberechtigt, pflegte ausschließlich seine eigene lokale
Sicht auf das Netz und war gleichzeitig Sender und Empfänger, ohne dass irgendwer stellvertretend
für alle eine "offizielle" Geräteliste oder Berechtigung verwaltete. **Seit Abschnitt 10 gibt es
eine Admin-Rolle mit exklusiver Konfigurationshoheit; die dezentrale P2P-Netzwerkkommunikation
ohne zentralen Server bleibt davon unberührt bestehen** (siehe FR-33, ersetzt FR-26/Abschnitt 3.6).

**Lizenz: Open Source, MIT.** Feste Vorgabe der Stadt als Auftraggeberin. Der Quellcode ist frei einsehbar und nutzbar; Einnahmen entstehen über den Dienstleistungsvertrag mit der Stadt (Bereitstellung, Wartung, Support), nicht über eine Nutzungsgebühr auf den Code selbst. Wichtige Konsequenz: alles, was ins öffentliche Repository kommt, ist für jeden einsehbar – siehe 3.8 und 5.10 für den Umgang mit dem privaten Signierschlüssel des separaten Lizenzierungs-Mechanismus.

## 2. Nicht-Ziele / Abgrenzung

- Kein Cloud-Dienst, keine externe Kommunikation, keine Internetanbindung notwendig
- Kein Video-/Audio-Chat, keine Fernsteuerung, keine Bildschirmüberwachung
- ~~Keine zentrale Verwaltungsinstanz, kein Admin-Konto, keine Berechtigungsliste~~ — **überholt seit Abschnitt 10**: Es gibt jetzt eine Admin-Rolle mit Konfigurationshoheit (FR-33). Weiterhin gültig bleibt: kein zentraler *Server*-Prozess, keine Fernsteuerung, keine Bildschirmüberwachung.
- Kein Ersatz für einen echten Notruf/Sicherheitsdienst bei akuter Gefahr – ergänzendes internes Tool

## 3. Funktionale Anforderungen

### 3.1 Ersteinrichtung pro Arbeitsplatz
- FR-1 (**ersetzt durch FR-36 bis FR-39**): Beim allerersten Start fragt das Programm Arbeitsplatz-Name, Nutzer und Raum ab – Pflichtangaben, bevor sich das Gerät im Netz bekannt gibt. Seit Abschnitt 10 gilt stattdessen: Gerätename automatisch aus Windows, Raumname/-nummer Pflicht im Admin-Installer, User-Konten fragen gar nichts mehr ab.
- FR-2 (**ersetzt durch FR-42**): Das Tastenkürzel wird initial mit abgefragt, kann aber übersprungen und später im Konfigurationsprogramm nachgeholt werden. Seit Abschnitt 10 legt der Admin den Standard-Hotkey pro Kreis fest, User-Konten konfigurieren nichts selbst.
- FR-3: Beim ersten Start wird eine feste, eindeutige Geräte-Kennung erzeugt und dauerhaft lokal gespeichert (nicht die IP-Adresse) – dient dem korrekten Wiedererkennen von Einträgen bei allen anderen Geräten
- FR-4: Die Installation legt eine Verknüpfung für das Konfigurationsprogramm im Windows-Startmenü an

### 3.2 Auslösung
- FR-5 (**ergänzt durch FR-43/FR-44**): Das konfigurierte Tastenkürzel sendet die hinterlegte Alarmnachricht direkt an alle Geräte mit gesetztem Haken "wird benachrichtigt" – ohne Zwischenfenster oder Zielauswahl im Moment des Auslösens. Der Empfängerkreis ist seit Abschnitt 10 pro Sender/Profil individuell (asymmetrisch) konfigurierbar statt nur über einen globalen Haken.
- FR-6: Das Tastenkürzel funktioniert systemweit, auch über Vollbildanwendungen
- FR-7 (**Produktentscheidung weiterhin offen**, siehe 8a): Sicherheitsabfrage "Wirklich senden?" vor dem tatsächlichen Versenden – ja oder nein
- FR-8 (**gelöst durch FR-43, nicht mehr offen**): aktuell eine feste Alarmnachricht pro Arbeitsplatz; ob mehrere unterschiedliche Alarme mit eigenen Nachrichten/Tastenkürzeln unterstützt werden sollen, ist offen. Antwort: ja — mehrere Alarm-Profile (z. B. "Notfall Nachbar", "Notfall alle") mit jeweils eigenem Hotkey und eigenem Sender-/Empfängerkreis sind Teil von Abschnitt 10.

### 3.3 Empfang & Rückmeldung
- FR-9: Popup erscheint erzwungen im Vordergrund beim Empfänger, unabhängig vom aktuell aktiven Fenster
- FR-10: Popup muss aktiv quittiert werden, bevor es verschwindet
- FR-11: Signalton spielt auf allen aktiven Audio-Ausgabegeräten, auch bei Stummschaltung/Kopfhörer (temporäre Mute-/Lautstärke-Übersteuerung, danach Wiederherstellung des ursprünglichen Zustands)
- FR-12: Der abgespielte Signalton ist eine reine Empfänger-Einstellung – gilt gleich für alle eingehenden Alarme, unabhängig davon, wer sie schickt
- FR-13: Jedes Empfängergerät sendet nach Anzeige des Popups automatisch eine kurze Empfangsbestätigung an den Absender zurück
- FR-14 (**erweitert durch FR-53**): Der Absender sieht die Bestätigungen als kleines, halbtransparentes Info-Banner in einer Bildschirmecke ("Empfangen: x"); bleibt stehen, bis es manuell weggeklickt wird, kein automatisches Ausblenden. Seit Abschnitt 10 zusätzlich mit "Auf dem Weg"-Namensliste und Abbrechen-Button.
- FR-15 (**umgesetzt durch FR-51**, nicht mehr nur vorgemerkt): zweistufige Bestätigung – zusätzlich zur Anzeige-Bestätigung eine zweite, sobald das Popup aktiv quittiert wurde (FR-10). Umgesetzt als "bin unterwegs"-Rückmeldung in Abschnitt 10.

### 3.4 Konfigurationsprogramm (ein einziges Fenster)
- FR-16: Ein einziges Konfigurationsprogramm, getrennt vom Hintergrunddienst, erreichbar über die Startmenü-Verknüpfung
- FR-17: Bereich "Allgemein" – betrifft nur den eigenen Arbeitsplatz: Name/Nutzer/Raum (editierbar; Änderung löst erneuten Broadcast und Überschreiben bei allen anderen Geräten aus), eigenes Tastenkürzel, eigene Alarmnachricht, eigener Signalton für eingehende Alarme
- FR-18: Bereich "Individuell" – Liste aller bekannten anderen Geräte, pro Zeile: Favorit-Markierung, Checkbox "wird benachrichtigt", Notiz (Freitext); Name/Nutzer/Raum werden nur angezeigt (vom jeweiligen Gerät selbst geliefert), hier nicht editierbar
- FR-19: Favoriten stehen oben in der Liste
- FR-20: Ein "Erneut suchen"-Button stößt manuell eine zusätzliche Broadcast-Anfrage an (Fallback zu 3.5)

### 3.5 Geräte-Erkennung
- FR-21 (**erweitert durch FR-54**): Jedes Gerät sendet bei **jedem eigenen Start** (nicht nur beim allerersten) einmalig seine Identität (Geräte-Kennung, Name, Nutzer, Raum, IP) per Broadcast ins Subnetz. Seit Abschnitt 10 zusätzlich mit Kunden-/Gruppen-ID, Programmversion und Config-Version.
- FR-22: Jedes bereits laufende Gerät, das diesen Start-Broadcast empfängt, nimmt den Absender in die eigene Liste auf bzw. aktualisiert den bestehenden Eintrag (Abgleich über die Geräte-Kennung, nicht über die IP) und antwortet direkt mit der eigenen Identität zurück – so lernt auch das neu gestartete Gerät alle bereits laufenden Geräte, ohne dass irgendwer das Konfigurationsprogramm öffnen muss
- FR-23: Kein Heartbeat – der Austausch findet ausschließlich beim Start bzw. bei manuellem "Erneut suchen" statt
- FR-24: Neu entdeckte Geräte erscheinen unmittelbar als hervorgehobene Zeile (z. B. "Neu"-Kennzeichnung) direkt in der bestehenden Liste – kein separates Popup, keine Snooze-Funktion; Favorit, Checkbox und Notiz sind sofort direkt in der Zeile setzbar
- FR-25: Die "Neu"-Hervorhebung verschwindet, sobald die Checkbox "wird benachrichtigt" für dieses Gerät einmal bewusst gesetzt oder aktiv gelassen wurde

### 3.6 Keine zentrale Berechtigung (**vollständig ersetzt durch Abschnitt 10.1/10.3, siehe FR-33**)
- FR-26 (**ersetzt durch FR-33**): Es gibt keine Berechtigungsliste und keine Admin-Rolle. Jede Person mit installierter und eingerichteter Software kann Alarme auslösen und wird Empfänger, sobald sie bei anderen Geräten den Haken "wird benachrichtigt" trägt. Eine zentrale Berechtigungsprüfung ist mit dem gewählten Architekturprinzip (kein Admin, keine zentrale Instanz) nicht sinnvoll umsetzbar. **Nach Rücksprache mit dem Auftraggeber ist diese Anforderung überholt** — es gibt jetzt eine Admin-Rolle mit Konfigurationshoheit, User-Konten können weiterhin nur Empfänger und ggf. Sender sein, aber nicht mehr konfigurieren.

### 3.7 Selbsttest & Installation
- FR-27: Selbsttest-Funktion im Programm ("Testalarm an mich selbst senden")
- FR-28 (**ersetzt durch FR-57 bis FR-61**): Der Installer erkennt eine bereits vorhandene Installation (z. B. über eine gespeicherte Versionskennung) und vergleicht sie mit der Version des aktuellen Installers; ist der Installer neuer, wird ein Update angeboten. Seit Abschnitt 10 ersetzt durch eine vollautomatische, signierte 0-Downtime-Update-Pipeline statt eines manuell angebotenen Updates beim erneuten Installer-Start.

### 3.8 Bildschirmschoner
- FR-29: Ein aktiver Bildschirmschoner (nicht Sperrbildschirm, nicht Ruhezustand/Energiesparmodus) wird beim Anzeigen eines eingehenden Alarms automatisch beendet bzw. gar nicht erst gestartet, damit das Popup sichtbar wird
- FR-30: Der Sperrbildschirm bleibt bewusst unangetastet – ist der Rechner gesperrt, wird das Popup nicht erzwungen angezeigt. Windows isoliert den gesperrten Zustand technisch bewusst so, dass kein normales Programm dort eingreifen kann; das ist eine Sicherheitsgrenze, kein Bug

### 3.9 Lizenzierung des Codes
- FR-31: Das komplette Projekt wird unter der MIT-Lizenz veröffentlicht (siehe 1.). Eine `LICENSE`-Datei mit dem MIT-Text gehört ins Repository
- FR-32: Der private Signierschlüssel für die separate Kunden-Lizenzverwaltung (Ed25519-signierte Lizenzdateien mit Kundenname/Sitzanzahl/Ablaufdatum, an anderer Stelle bereits besprochen) darf niemals im Repository landen – nur der öffentliche Schlüssel zur Prüfung ist Teil des Codes. Diese Lizenzverwaltung selbst ist nicht Teil dieses Pflichtenhefts und wird separat spezifiziert, sobald sie ansteht

## 4. Nicht-funktionale Anforderungen

- NFR-1: 24/7-Betrieb ohne geplante Ausfallzeiten, automatischer Neustart nach Absturz
- NFR-2: Autostart mit Nutzeranmeldung, kein manuelles Eingreifen nötig
- NFR-3: Keine Abhängigkeit von einem zentralen Server/Broker – Ausfall eines Endgeräts darf die Kommunikation der übrigen Geräte nicht beeinträchtigen
- NFR-4: Kommunikation ausschließlich innerhalb des LAN, keine Cloud-/Internetanbindung
- NFR-5: Datenminimierung – keine unnötige Protokollierung von Nachrichteninhalten
- NFR-6: Schutz gegen Fehlalarme/mutwillige Falschauslösung
- NFR-7: Nachvollziehbarkeit (wer hat wann welchen Alarm ausgelöst), aber datensparsam protokolliert
- NFR-8: Latenz Ziel < 2 Sekunden von Auslösung bis Anzeige beim Empfänger
- NFR-9: Automatisierte Selbsttests im Claude-Code-Projekt für die Kernfunktionen (Verbindungsaufbau, Nachrichtenformat, Start-Broadcast/Antwort, Geräte-Kennung-Abgleich, Konfigurationsspeicherung, "Neu"-Zustand)
- NFR-10: Die Architektur ist bewusst erweiterbar für die beiden noch offenen Entscheidungen (FR-7, FR-8) zu bauen – klare Erweiterungspunkte/Feature-Flags statt harter Verdrahtung, siehe 5.10
- NFR-11 (neu, Version 2): Programm-Updates werden ausschließlich als signiert erkannte Pakete akzeptiert und gestaffelt vom Admin freigegeben (FR-57, FR-59) – ein fehlerhaftes Update darf sich nicht unkontrolliert netzwerkweit verbreiten
- NFR-12 (neu, Version 2): Konfigurationsänderungen wirken sich per Hot-Reload unmittelbar aus, ohne dass ein Gerät neu gestartet werden muss (FR-55)
- NFR-13 (neu, Version 2, ergänzt NFR-5/NFR-7): Die Reaktions-/Namensanzeige bei Alarmen (FR-51/FR-53) sowie die RDP-Kennzeichnung (FR-41) sind zusätzliche personenbezogene Verarbeitung gegenüber Version 1 und daher gesondert mit dem Personalrat abzustimmen (siehe Abschnitt 8)

## 5. Empfohlene Architektur

### 5.1 Kommunikationsmodell
Kein Broker, kein Mesh-Routing über Drittgeräte. Jedes Endgerät ist gleichzeitig TCP-Server und -Client. Der Sender öffnet beim Auslösen direkte TCP-Verbindungen zu den gewählten Ziel-IPs.

### 5.2 Komponenten pro Endgerät
- Listener-Dienst (TCP-Server; nimmt Alarmnachrichten entgegen, sendet Empfangsbestätigung zurück, sendet/empfängt Start-Broadcasts)
- Sender-Funktion, ausgelöst durch Hotkey, sendet direkt ohne Zwischenfenster
- Konfigurationsprogramm (ein Fenster, siehe 3.4) — **für User-Konten seit Abschnitt 10 entfallen**, siehe 10.3 für das Admin-Dashboard
- **Neu (Version 2)**: Windows-Dienst (LocalService/SYSTEM), ausschließlich zuständig für Installieren/Testen/Swappen/Deinstallieren beim Auto-Update (FR-61); Kommunikation mit der User-App lokal über Named Pipes, kein Netzwerk-Traffic zwischen Dienst und App

### 5.3 Technologie-Empfehlung
- .NET 8, C#, WPF für die GUI
- `System.Net.Sockets` für die Nachrichtenübertragung, optional TLS mit selbstsigniertem Zertifikat
- NAudio (MIT-lizenziert) für Mehrgeräte-Audioausgabe inkl. Mute-Override über WASAPI
- Globaler Hotkey über die Win32-API (`RegisterHotKey`)
- Forced-Foreground: bekannte Windows-Workarounds nötig (`AttachThreadInput`-Trick bzw. `SPI_SETFOREGROUNDLOCKTIMEOUT`) – auf allen im Netz vorhandenen Windows-Versionen testen
- Geräte-Kennung: GUID, beim ersten Start erzeugt, lokal gespeichert
- Autostart: Task-Scheduler-Task "Bei Anmeldung" pro Nutzer statt echtem SYSTEM-Dienst (vermeidet Session-0-Isolation)

### 5.4 Datenhaltung
Lokale Geräteliste (JSON), dauerhaft gespeichert, Abgleich über die Geräte-Kennung. Pro Eintrag: Geräte-Kennung, Name, Nutzer, Raum, IP (wird bei jedem Broadcast/Response aktualisiert), Favorit, "wird benachrichtigt", Notiz, Status "neu" (bis zur ersten bewussten Entscheidung, FR-25). Keine separate Berechtigungsdatei mehr nötig (siehe 3.6).

### 5.5 Geräte-Erkennung
- **Start-Broadcast**: jedes Gerät sendet bei jedem eigenen Start einmalig seine Identität
- **Antwort**: jedes empfangende Gerät aktualisiert seinen Eintrag und antwortet direkt mit der eigenen Identität zurück
- **Kein Heartbeat**, kein Dauerbetrieb
- **Manueller Fallback**: "Erneut suchen" im Konfigurationsprogramm für den Fall, dass ein Gerät z. B. seit Tagen durchläuft und in der Zwischenzeit neue Geräte dazukamen
- Funktioniert nur innerhalb desselben Subnetzes/VLANs – bei getrennten Netzsegmenten bleibt die manuelle Eintragung als Fallback nötig
- Das eigentliche Alarm-Senden ist davon unabhängig: Es nutzt beim Auslösen die zuletzt bekannte IP zur jeweiligen Geräte-Kennung

### 5.6 Empfangsbestätigung
Kleines, halbtransparentes Eck-Banner beim Absender ("Empfangen: x"), bleibt bis manuell weggeklickt.

### 5.7 Autostart- und Startmenü-Registrierung
Der Hintergrunddienst trägt sich beim ersten Start selbst in den Task-Planer ein ("bei Anmeldung ausführen", pro Nutzer, kein SYSTEM-Kontext nötig). Das braucht in der Regel keine Admin-Rechte – *sofern* die Gruppenrichtlinien im Verwaltungsnetzwerk das normalen Nutzer:innen erlauben; andernfalls muss der Eintrag zentral per GPO ausgerollt werden (siehe Abschnitt 8). Die Startmenü-Verknüpfung fürs Konfigurationsprogramm (FR-4) wird vom Installer angelegt, unabhängig vom Autostart-Eintrag des Hintergrunddienstes.

### 5.8 Installer & Updates
Der Installer speichert beim Installieren eine Versionskennung lokal (z. B. Registry-Eintrag oder kleine Versionsdatei). Bei erneuter Ausführung eines Installers prüft dieser, ob bereits eine Installation vorhanden ist und ob seine eigene Version neuer ist; falls ja, wird ein Update angeboten statt einer Neuinstallation. Der Hintergrunddienst muss beim Update sauber gestoppt und neu gestartet werden, ohne die lokale Geräteliste zu verlieren. Empfehlenswerte Werkzeuge: Inno Setup oder WiX, die Versionsvergleich und Update-Erkennung eingebaut unterstützen.

### 5.9 Erweiterbarkeit (wichtig für die Entwicklung mit Claude Code)
FR-7 (Sicherheitsabfrage) und FR-8 (mehrere Nachrichten) sind aktuell offen. Die Architektur soll so gebaut werden, dass beides sich später einfügen lässt, ohne bestehende Teile umzubauen:
- Die Sende-Funktion bekommt von Anfang an einen optionalen "Vor dem Senden bestätigen"-Hook, der aktuell einfach übersprungen wird (Standard: aus)
- Nachricht/Hotkey werden intern bereits als Liste geführt (auch wenn die Oberfläche aktuell nur eine einzige anzeigt und nur einen Eintrag zulässt), damit eine spätere Erweiterung auf mehrere Alarme keine Datenmodell-Änderung mehr braucht

### 5.10 Repository-Hygiene (Open Source)
Da der komplette Code öffentlich einsehbar ist (FR-31), gilt für alles, was ins Repository kommt, dieselbe Regel wie für jeden anderen Secret-Umgang: keine privaten Schlüssel, keine Zertifikate, keine Zugangsdaten im Klartext, auch nicht in der Commit-Historie. Der private Ed25519-Schlüssel für die Kunden-Lizenzverwaltung (FR-32) bleibt außerhalb des Repos, z. B. lokal beim Entwickler oder in einem separaten, nicht-öffentlichen Speicherort. Eine `.gitignore`, die typische Secret-Dateimuster ausschließt, sollte von Anfang an Teil des Projekts sein.

## 6. Bekannte technische Herausforderungen (explizit an Claude Code mitgeben)
- Foreground-Stealing-Prevention von Windows umgehen, inkl. Test gegen Vollbildanwendungen
- Mehrere Audio-Ausgabegeräte gleichzeitig ansteuern und Mute-/Lautstärke-Zustand sauber zurücksetzen
- Windows-Firewall: Inbound-Regel je Endgerät für den gewählten Port, ggf. per GPO auszurollen
- Verhalten bei Bildschirmsperre/Energiesparmodus definieren; Bildschirmschoner gezielt beenden, ohne den Sperrbildschirm anzutasten (klare Trennung der beiden Zustände)
- Broadcast-Erkennung nur innerhalb desselben Subnetzes/VLANs zuverlässig – Verhalten bei segmentierten Netzen festlegen
- Geräte-Kennung robust erzeugen (Kollisionsfreiheit, GUID) und bei Neuinstallation/Update nicht versehentlich neu vergeben
- Empfangsbestätigungen zuverlässig zählen, auch wenn einzelne Verbindungen fehlschlagen oder Antworten verspätet eintreffen
- "Neu"-Zustand korrekt zurücksetzen, robust gegen Neustart des Konfigurationsprogramms
- Installer-Versionsvergleich zuverlässig implementieren; Update darf die lokale Geräteliste nicht löschen
- Selbstregistrierung im Task-Planer kann durch restriktive GPO-Einstellungen blockiert sein – als Testfall für den Rollout einplanen
- **Neu (Version 2)**: Race-Condition beim Exklusiv-Edit-Lock bei nahezu gleichzeitigem Zugriffs-Call zweier Admins (FR-48) sauber auflösen, inkl. Backoff-Retry ohne Endlosschleife
- **Neu (Version 2)**: Portkonflikt zwischen alter und neuer Instanz während des Update-Swaps zuverlässig vermeiden (FR-58); Named-Pipe-IPC zwischen Windows-Dienst und User-App robust gegen Neustart einer der beiden Seiten machen
- **Neu (Version 2)**: Gestaffelte Rollout-Logik (FR-59) darf sich nicht mit der Call-and-Response-Weiterverbreitung (FR-54) in eine unkontrollierte Lauffeuer-Ausbreitung verselbstständigen — Freigabekontingent pro Stufe muss beim Weiterverbreiten strikt geprüft werden
- **Neu (Version 2)**: Hot-Reload der Konfiguration (FR-55) darf laufende Alarme nicht unterbrechen oder inkonsistente Zwischenzustände erzeugen, falls eine Config-Änderung genau während eines aktiven Alarms eintrifft

## 7. Vergleich mit bestehenden Lösungen (kurz)
- `msg.exe` (Windows-Bordmittel): sofort verfügbar, aber ohne Audio-Override, ohne Raum-Mapping, abhängig von RPC/Firewall-Freigaben
- Veyon (Open Source, GPL, Windows): hat Nachrichten-Broadcast und Standort-/Raumverwaltung, ist aber als Klassenraum-/Fernwartungssoftware konzipiert – Akzeptanzrisiko bei Personalrat/Belegschaft, da leicht als Überwachungssoftware wahrgenommen
- ntfy (self-hosted): braucht einen zentralen Server, kein echtes „jeder ist Server“-Modell
- **Fazit**: Eigenentwicklung ist nötig, da die Kombination aus erzwungenem Vordergrund, Audio-Override, Broker-Losigkeit und dezentraler Selbstverwaltung so nicht fertig verfügbar ist

## 8. Offene organisatorische Punkte (vor Rollout extern klären)

- Datenschutzbeauftragte:n der Behörde einbinden (Verfahrensverzeichnis, da personenbezogene Daten wie Nutzername/Gerätezuordnung verarbeitet werden)
- Prüfen, ob Personalrats-Beteiligung erforderlich ist: Systeme, die *geeignet* sind, Verhalten oder Leistung von Beschäftigten zu überwachen, sind in der Regel mitbestimmungspflichtig. Ein Auslösungs-Log kann darunterfallen. Landesspezifisches Personalvertretungsgesetz prüfen, das ist keine Rechtsberatung, nur ein Hinweis, den man nicht übersehen sollte. **Ergänzung Version 2**: Die neue Reaktions-/Namensanzeige bei Alarmen und die RDP-Kennzeichnung (NFR-13) sind eine funktionale Erweiterung der ursprünglich besprochenen Protokollierung und sollten dem Personalrat gesondert vorgelegt werden, nicht stillschweigend unter der alten Zustimmung mitlaufen
- IT-Sicherheitsbeauftragte:n bzw. GPO-Verantwortliche für Firewall-Freigaben frühzeitig einbinden. **Ergänzung Version 2, zwei konkrete neue Fragen:**
  - Funktioniert die Broadcast-basierte Geräteerkennung/-alarmierung/-update-Verteilung über mehrere Netzsegmente (VLANs)/Standorte hinweg, oder ist das Verwaltungsnetz getrennt segmentiert?
  - Ist das eigenständige, signierte Peer-to-Peer-Update-Verfahren (FR-57 bis FR-61) mit der IT-Sicherheitsrichtlinie der Behörde vereinbar, oder wird eine Einbindung in eine bestehende zentrale Softwareverteilung (z. B. WSUS) vorausgesetzt?
- Test-/Rollout-Plan für unterschiedliche Windows-Versionen im Netzwerk aufstellen

## 8a. Offene Produktentscheidungen (direkte Entscheidung, keine Rückfrage bei Dritten nötig)

- **Sicherheitsabfrage** (weiterhin offen): Soll vor dem Absenden eines Alarms eine Bestätigung kommen ("Wirklich senden?"), oder soll es sofort losgehen, damit im Ernstfall keine Zeit verloren geht?
- ~~Mehrere Nachrichten~~ — **entschieden, siehe FR-8/FR-43**: Ja, mehrere Alarm-Profile mit eigenem Hotkey und eigenem Sender-/Empfängerkreis sind Teil von Abschnitt 10.

### 8a-2. Architekturentscheidungen aus Version 2 (Claude-Empfehlung, vom Auftraggeber angenommen)
- Rechte-Trennung beim Auto-Update über einen separaten Windows-Dienst statt erhöhter Rechte für die User-App selbst (FR-61). Bei Bedarf hier nochmal gezielt nachfragen, falls das im Umsetzungs-Prompt anders gewünscht ist.

### 8a-3. Vom Auftraggeber angekündigt für nach Abschluss des Alpha-Tests (noch nicht umzusetzen)
- **System-Tray-Icon**: Nach der Alpha-Testphase soll die laufende App (aktuell bewusst ohne eigenes Fenster/Tray-Icon, siehe 5.3) unten im Info-Bereich der Taskleiste laufen; das Admin-Dashboard soll zusätzlich von dort aus erreichbar sein (bisher nur über den Start-Menü-Eintrag "HälpMi Dashboard").
- Sobald dieser Punkt ansteht, bespricht Claude Code vorher gemeinsam mit dem Auftraggeber weitere Optimierungsvorschläge, statt direkt umzusetzen.
- **Priorität aktuell (Stand Alpha-Test)**: zuerst der Fix des Absturzes beim Öffnen des Admin-Dashboards, danach ein vollständiger Test aller MVP-Funktionen. Dieser Punkt hier ist bewusst zurückgestellt.
- **Admin-Dashboard UX-Nachschärfung** (Rückmeldung 04.08.2026, bewusst zurückgestellt bis nach der aktuellen Testrunde): Feldbeschriftungen in den Kreise-/Gruppen-/Alarm-Profile-Detailbereichen sollen eindeutiger/sinnhafter werden; beim Klicken in ein leeres rechtes Detailfeld (kein Kreis/keine Gruppe/kein Profil ausgewählt) soll automatisch ein neues Element angelegt werden, statt dass der Bereich deaktiviert bleibt; allgemein Klicks reduzieren, wo es geht.

## 9. Vorschlag für phasiges Vorgehen mit Claude Code

**Phase 1 – abgeschlossen:**
1. Grundkommunikation: direkte TCP-Verbindung, Geräte-Kennung, Start-Broadcast + Antwort
2. Forced Foreground + Mehrgeräte-Audio inkl. Mute-Override
3. Empfangsbestätigung + Eck-Banner beim Absender
4. Konfigurationsprogramm: ein Fenster mit Bereich "Allgemein" und "Individuell", Favoriten, "Neu"-Hervorhebung inline
5. Ersteinrichtung, Geräte-Kennung-Erzeugung, Startmenü-Verknüpfung, Autostart-Selbstregistrierung
6. Selbsttest-Funktion, automatisierte Tests
7. Installer mit Versionserkennung/Update-Angebot
8. Bildschirmschoner-Handling (beenden bei eingehendem Alarm, Sperrbildschirm unangetastet)
9. MIT-`LICENSE`-Datei, `.gitignore` gegen Secrets, öffentlicher Schlüssel für die Kunden-Lizenzverwaltung im Code, privater Schlüssel bewusst außerhalb des Repos
10. Erweiterungspunkte für FR-7/FR-8 im Code vorbereiten (siehe 5.9), aber inaktiv/mit einer festen Nachricht ausliefern

**Phase 2 – aktuell, siehe Abschnitt 10 und `claude-code-prompt-teil2-admin-update.md`:**
Admin-/User-Rollen, Ersteinrichtung über Admin-Installer, Admin-Dashboard mit Exklusiv-Edit-Lock,
Gruppen-/Kunden-Isolation, erweitertes Alarmverhalten (Priorität-0-Fenster, "bin unterwegs",
Auto-Update-Pipeline mit gestaffeltem Rollout, Install-Creator. Details siehe Abschnitt 10.

**Phase 3 – nach Antwort auf die verbliebene Frage in 8a:**
11. Sicherheitsabfrage ein-/ausschaltbar gemäß Entscheidung zu FR-7 umsetzen
12. Rollout-Test in der Zielumgebung auf allen vorhandenen Windows-Versionen, inkl. der in Abschnitt 8 ergänzten IT-Fragen (VLAN/Mehrstandort, WSUS-Vereinbarkeit)

## 10. Update Version 2 (02.08.2026) — Admin-Rollen, Konfigurationsverwaltung, Auto-Update

Entstanden aus einem Rücksprachegespräch mit dem Auftraggeber. Ersetzt die in den Abschnitten 1–9
markierten Einzelpunkte, siehe dortige Verweise. Der zugehörige Umsetzungs-Prompt für Claude Code
ist `claude-code-prompt-teil2-admin-update.md`, die dauerhaften Architekturregeln stehen in
`CLAUDE.md`.

### 10.1 Rollen & Installation
- FR-33: Es gibt zwei Rollen, Admin-Konto und User-Konto, als App-interne Eigenschaft in der Lizenz-/Konfigdatei hinterlegt — unabhängig von Windows-eigenen Benutzerrechten. Ein Windows-Standardnutzer kann App-Admin sein, ein Windows-Admin muss es nicht sein. **Ersetzt FR-26/Abschnitt 3.6.**
- FR-34: Installation erfolgt für alle Windows-Benutzerkonten des Geräts (Machine-Scope, nicht User-Scope), inklusive entsprechend gründlichem Uninstaller für alle Konten (Anforderung: so gründlich wie Revo Uninstall)
- FR-35: Es gibt zwei Installer-Typen, Admin-Installer und User-Installer, verknüpft über eine gemeinsame Kunden-/Gruppen-ID (siehe FR-49). Der Admin-Installer bringt sämtliche Funktionen der User-App mit, jede Funktion einzeln aktivierbar/deaktivierbar, sodass der Admin-Rechner zugleich als Testgerät dienen kann

### 10.2 Ersteinrichtung (**ersetzt FR-1, FR-2**)
- FR-36: Gerätename wird automatisch aus dem Windows-Computernamen übernommen, nicht editierbar
- FR-37: Raumname und Raumnummer sind Pflichtfelder, im Admin-Installer abgefragt; ohne Eingabe ist die Installation nicht abschließbar. Bei der Raumnummer-Vergabe im Admin-Dashboard werden bereits vergebene Raumnamen zu bestehenden Raumnummern als Vorschlag angezeigt, um Räume mit mehreren PCs konsistent zu benennen
- FR-38: Das Benutzerkonto wird dynamisch aus dem aktuell angemeldeten Windows-User übernommen, keine manuelle Eingabe
- FR-39: Der User-Installer fragt keine dieser Angaben ab — die reguläre Konfiguration entfällt für User-Konten vollständig, das übernimmt ausschließlich der Admin über das Dashboard (siehe FR-33)
- FR-40: Anzeige beim Empfänger: Raum + Raumnummer immer prominent sichtbar, Benutzername klein und untergeordnet
- FR-41: Sitzungen, die per RDP verbunden sind, werden lokal über `GetSystemMetrics(SM_REMOTESESSION)` erkannt und zusätzlich zum Benutzernamen gekennzeichnet (z. B. "(Remote)"), um Homeoffice- von Vor-Ort-Sitzungen zu unterscheiden — reine lokale API-Abfrage, keine externe Abhängigkeit oder IT-Auskunft nötig

### 10.3 Admin-Dashboard (**ersetzt 3.4/FR-16 bis FR-20 für Admin-seitige Konfiguration**)
- FR-42: Standard-Hotkey-Kombination pro Kreis festlegbar, Ton konfigurierbar
- FR-43: Mehrere Alarm-Profile parallel möglich (z. B. "Notfall Nachbar", "Notfall alle"), jedes Profil mit eigenem Hotkey und eigenem Sender-/Empfängerkreis (**löst FR-8 auf, siehe 8a**)
- FR-44: Der Empfängerkreis pro Sender ist asymmetrisch konfigurierbar: pro Profil und pro Sender (Einzelgerät oder Gruppe) ein individueller Empfängerkreis, auch gruppenübergreifend. UI: pro Profil eine Liste aller Sender/Gruppen mit einer Zelle für den zugehörigen Empfängerkreis; Klick öffnet eine Auswahlansicht mit Drag-and-Drop-Zuordnung (primär) bzw. Checkboxen (Fallback für Barrierefreiheit)
- FR-45: Ein "Alle"-Button wendet eine Einstellung gleichzeitig auf mehrere Sender/Empfänger an und warnt vor dem Anwenden, dass bestehende individuelle Konfigurationen überschrieben werden
- FR-46: Gruppenverwaltung — mehrere Kreise lassen sich zu Gruppen zusammenfassen, um die Administration bei vielen Kreisen zu vereinfachen (z. B. "Haus 1 Etage 1" als Gruppe mit eigenem Empfängerkreis)
- FR-47: Der Schwellwert X (ab wie vielen Antworten das Empfänger-Alarmfenster ignorierbar wird, siehe FR-51) ist pro Admin und pro einzelnem Alarm-Ereignis/Trigger konfigurierbar, nicht global fix

### 10.4 Exklusiv-Edit-Lock
- FR-48: Beim Öffnen des Admin-Dashboards sendet das Gerät einen TCP-Call "will editieren" an alle im Kreis erreichbaren admin-fähigen Geräte. Antwortet eines "wird bereits administriert" → Zugriff verweigert (Meldung im Stil des regulären Alarm-Popups); sonst wird Zugriff gewährt und die Sperre gesetzt. Sperre gilt pro Kreis, nicht global, mehrere Admins können gleichzeitig an unterschiedlichen Kreisen arbeiten. Kollisionsfall (gleichzeitiger Call): beide lehnen sich gegenseitig ab, geben sofort wieder frei, Retry nach zufälligem Backoff (200–800 ms). Automatische Freigabe nach 10 Minuten ohne Edit-Aktivität

### 10.5 Gruppen-/Kunden-Isolation
- FR-49: Beim Bauen im Install-Creator wird aus dem eingegebenen Kundennamen eine feste Kunden-/Gruppen-ID abgeleitet und in Admin- **und** den zugehörigen User-Installer eingebettet. Jedes Netzwerkpaket (Boot-Call, Alarm, Config-Sync, Update-Ankündigung) trägt diese ID; Pakete mit abweichender ID werden ohne Reaktion verworfen. Dadurch können zwei unabhängige Installationsgruppen im selben physischen Netz koexistieren, ohne sich zu beeinflussen

### 10.6 Alarm-Verhalten (**erweitert 3.2/3.3**)
- FR-50: Sender — Hotkey löst wiederholtes Senden im 5-Sekunden-Takt aus, automatischer Stopp nach 5 Minuten oder sobald mindestens 2 Empfänger mit "bin unterwegs" reagiert haben (identisches Ereignis wie "ich komme"). Manueller Abbrechen-Button verfügbar
- FR-51: Empfänger — Alarmfenster mit Priorität 0, nicht über normale Fenstersteuerung deaktivierbar. Schließbar erst ab X Antworten (FR-47); wird es dennoch vorzeitig geschlossen, öffnet es sich beim nächsten empfangenen Alarmsignal wieder automatisch. Button "bin unterwegs" sendet Rückmeldung (Raum + Bezeichnung + Status) an den Sender (**löst FR-15 ein**)
- FR-52: Auto-Schließen beim Empfänger 1 Minute nach dem jeweils letzten empfangenen Alarmsignal — nicht ab dem ersten Signal, greift also erst, nachdem der Sender aufgehört hat zu senden (manueller Abbruch, 2 Bestätigungen erreicht oder 5-Minuten-Timeout)
- FR-53: Info-Hover-Popup beim Sender zeigt zusätzlich zur Empfangsbestätigung (FR-14) eine formatierte Liste "Auf dem Weg" mit Usernamen untereinander

### 10.7 Boot-Verhalten (**erweitert 3.5**)
- FR-54: Der Boot-Broadcast (FR-21) trägt zusätzlich Kunden-/Gruppen-ID, Programmversion und Config-Version und ist als Call-and-Response angelegt: beide beteiligten Geräte tauschen ihren vollständigen Info-Stack aus, unabhängig davon, wer Absender des ursprünglichen Calls war. Das Gerät mit der jeweils neueren Version signalisiert dem anderen ein verfügbares Update

### 10.8 Konfigurations-Sync (Hot-Reload)
- FR-55: Konfigurationsänderungen werden bei Blur eines gültig ausgefüllten Pflichtfelds automatisch gespeichert und per Broadcast (inkl. neuer Config-Versionsnummer) verteilt — kein Save-Button. Empfangende Geräte laden die Konfiguration im laufenden Prozess neu (Hot-Reload) — **keine** Parallelinstanz, kein Swap, das ist ausschließlich der Update-Pipeline (10.9) vorbehalten
- FR-56: Änderungshistorie pro Kreis (letzte n Einträge) mit Undo-Möglichkeit

### 10.9 Programm-Update-Pipeline (0-Downtime, **ersetzt FR-28**)
- FR-57: Update-Pakete sind mit einem separaten Update-Signaturschlüssel signiert, getrennt vom Lizenzschlüssel (FR-32). Ungültig signierte Pakete werden verworfen
- FR-58: Ablauf pro Gerät: (1) neue Version wird als parallele Instanz installiert, alte Instanz behält den Produktiv-Port; (2) lokaler Testping auf separatem Test-Port; (3) zusätzliche Bestätigung von mindestens einem Peer über erfolgreiche Testkommunikation; (4) bei Erfolg übernimmt die neue Instanz den Produktiv-Port, die alte wird gestoppt und deinstalliert; (5) danach regulärer Boot-Call der neuen Instanz, wodurch sich das Update weiterverbreitet; (6) bei Fehler in einem der Schritte deinstalliert sich das Update selbst, sperrt sich 24 Stunden für erneute Versuche auf diesem Gerät und sendet Fehlermeldung inkl. Log/Systeminfo an den Admin
- FR-59: Gestaffelter Rollout pro Kreis, unabhängig von anderen Kreisen entscheidbar: Admin gibt Freigabestufen frei (z. B. 1 → 2 → 4 → 8 Geräte); ein Gerät gibt ein Update beim Boot-Call nur weiter, wenn es selbst freigegeben ist und das aktuelle Kontingent der Stufe nicht ausgeschöpft ist; Not-Aus stoppt die gesamte Verteilung bei mehreren Fehlschlägen innerhalb einer Stufe; pro Gerät maximal x Wiederholungsversuche, danach Blockade + Log + Admin-Info
- FR-60: Zufälliger Jitter (wenige Sekunden bis 1 Minute) vor dem tatsächlichen Update-Pull nach einem Boot-Call, um Lastspitzen bei gleichzeitigem Hochfahren vieler Geräte zu vermeiden
- FR-61 (**Architekturentscheidung, siehe 8a-2**): Rechte-Trennung über einen separaten Windows-Dienst (LocalService/SYSTEM), zuständig ausschließlich für Installieren/Testen/Swappen/Deinstallieren. Kommunikation Dienst ↔ User-App ausschließlich lokal über Named Pipes. Die User-App selbst bleibt weiterhin ohne erhöhte Rechte

### 10.10 Install-Creator (separates Entwickler-Tool, nicht Teil der Kunden-Installation)
- FR-62: Eigene ausführbare Datei im Projektverzeichnis baut das komplette Produkt als Admin-Installer
- FR-63: Checkbox "Test-Installer" macht das Installer-Passwort optional; bei regulärem Prod-Build ist das Passwort Pflicht, zusätzlich ist ein Kundenname Pflicht (daraus wird die Kunden-/Gruppen-ID abgeleitet, FR-49)
- FR-64: Alle Passwortfelder (Install-Creator wie Admin-Dashboard) erhalten einen "Zufällig generieren"-Button (16–24-stellig) mit direkt danebenliegendem "In Zwischenablage kopieren"-Icon. Passwörter werden nirgends zentral gespeichert — bei Verlust wird ein neuer Installer gebaut
- FR-65: Der Admin-Installer hat einen Button "User-Installer erstellen", der den zugehörigen User-Installer mit derselben Kunden-/Gruppen-ID erzeugt; optionales Installer-Passwort, vor Ort vom jeweiligen System-Admin vergebbar
- FR-66 (**bewusst zurückgestellt, nicht Teil der aktuellen Umsetzung**): zeitlich befristete Demo-Installer (z. B. X Wochen Laufzeit). Wird eigenständig spezifiziert, sobald Licensing/Pricing final steht

### 10.11 Ausdrücklich nicht Teil von Version 2
Folgende Punkte hängen von noch offenen externen Antworten ab (siehe Abschnitt 8) und werden
bewusst nicht vorab architektonisch festgelegt, aber modular genug gebaut, dass sie sich später
einfügen lassen:
- Verhalten bei Mehrfach-VLAN-/Mehrstandort-Netzen
- Etwaige Integration in eine zentrale Softwareverteilung der Behörde (WSUS o. ä.) als Alternative zur P2P-Update-Verteilung
