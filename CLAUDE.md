# CLAUDE.md — HälpMi (Version 2)

Diese Datei ersetzt die Vorgängerversion vollständig. Sie wird bei jeder Claude-Code-Sitzung
automatisch gelesen und enthält die dauerhaft geltenden Architekturregeln. Der zugehörige
einmalige Task-Prompt für diese Erweiterung liegt in `claude-code-prompt-teil2-admin-update.md`.

**Wichtig:** Abschnitt "Architekturprinzipien" in Version 1 enthielt die Sätze "Kein Admin, keine
zentrale Instanz" und "kein Admin-Konto, keine zentrale Berechtigungsinstanz". Das ist mit diesem
Update **explizit aufgehoben** — nicht als Bug, sondern als bewusste Weiterentwicklung nach
Kunden-Rücksprache. Wenn du (Claude Code) auf alten Code, Kommentare oder Tests triffst, die noch
von der reinen Gleichberechtigt-Peer-Architektur ausgehen, markiere sie zur Überarbeitung statt sie
stillschweigend zu ignorieren.

## Referenzdokumente
- `pflichtenheft-lan-alarmierung.md` — Quelle der Wahrheit für die ursprünglichen FR-/NFR-Nummern.
- `claude-code-prompt-teil1.md` — erster Umsetzungs-Prompt (Grundfunktionen, P2P-Alarm, Screensaver).
- `claude-code-prompt-teil2-admin-update.md` — aktueller Umsetzungs-Prompt (Admin-Rollen,
  Config-Sync, Auto-Update). Bei Widersprüchen zwischen dieser CLAUDE.md und den Prompt-Dateien:
  CLAUDE.md gewinnt, weil sie die aktuellere, dauerhafte Regel ist.

## Tech-Stack
- .NET 8, C#, WPF
- `System.Net.Sockets` für Netzwerkkommunikation (P2P, kein zentraler Server-Prozess)
- NAudio (MIT) für Mehrgeräte-Audio
- Win32-API direkt für `RegisterHotKey`, Forced-Foreground-Workarounds, Task-Scheduler-Registrierung,
  `GetSystemMetrics(SM_REMOTESESSION)` zur RDP-Erkennung
- Windows-Dienst (LocalService/SYSTEM) **neu**: ausschließlich für Installieren/Testen/Swappen/
  Deinstallieren beim Auto-Update. Kommunikation Dienst ↔ User-App über Named Pipes (localhost),
  niemals TCP über das Netzwerk. Die eigentliche User-App bleibt rechtelos im User-Kontext.
- Installer: Inno Setup oder WiX, plus separates Entwickler-Tool "Install-Creator" (siehe unten)

## Lizenz & Secrets
- MIT-Lizenz (feste Vorgabe der Stadt als Auftraggeberin). `LICENSE`-Datei, `.gitignore` gegen
  Secret-Dateimuster von Anfang an.
- Drei getrennte kryptografische Schlüsselpaare, niemals verwechseln oder zusammenlegen:
  1. Kunden-Lizenzsignatur (Ed25519) — Soft-Expiry, kein Hard-Lock.
  2. Update-Signatur (Ed25519, **separater** Schlüssel) — nur signierte Programm-Updates werden
     von einem Client angenommen und weiterverteilt.
  3. Optional pro Kunde: Installer-Passwort (kein Schlüsselpaar, siehe Install-Creator).
- Nur die jeweiligen **öffentlichen** Schlüssel werden ins Repository/die Binary eingebettet.
  Private Schlüssel gehören nie ins Repo, nie ins Log, nie in eine Fehlermeldung.

## Architekturprinzipien — nicht verhandelbar

### Weiterhin gültig
- Kein zentraler Server-Prozess/Broker. Netzwerkkommunikation bleibt P2P zwischen Geräten.
- Geräte-Abgleich läuft über persistente Geräte-GUID, niemals über IP-Adresse.
- Kein Heartbeat. Netzwerkverkehr nur bei: eigenem Boot-Call, Alarmauslösung/-antwort,
  Config-Änderung, Update-Verteilung. Kein Polling im Leerlauf.

### Neu ab Version 2
- **Kunden-/Gruppen-ID**: Jedes Admin+User-Installer-Paar trägt eine feste, beim Bauen im
  Install-Creator generierte ID. Alle Netzwerkpakete (Boot-Call, Alarm, Config-Sync, Update)
  werden nach dieser ID gefiltert. Zwei unabhängige Installationsgruppen im selben physischen
  Netz dürfen sich nie gegenseitig beeinflussen.
- **Rollen**: Admin-Konto vs. User-Konto ist eine App-interne Rolle (im Lizenz-/Konfigfile
  verankert), unabhängig von Windows-eigenen Adminrechten. Ein Windows-Standardnutzer kann
  App-Admin sein, ein Windows-Admin muss es nicht sein.
- **Kreis vs. Gruppe (klargestellt 04.08.2026)**: "Kreis" bezeichnet ausschließlich die
  kundennummernbasierte Netz-Isolation (siehe Kunden-/Gruppen-ID oben) — automatisch
  eindeutig pro Installation, nie ein Dashboard-Objekt, keine Verwaltung nötig. "Gruppe"
  ist die einzige organisatorische Einheit im Admin-Dashboard: ein direkter Zusammenschluss
  von Geräten/Räumen (z. B. eine Etage). Frühere Entwürfe hatten "Kreis" fälschlich als
  eigene Dashboard-Zwischenebene zwischen Gerät und Gruppe implementiert — das ist korrigiert.
- **Exklusiv-Edit-Lock statt Datei-Lock**: beim Auswählen einer Gruppe oder eines
  Alarm-Profils zur Bearbeitung sendet das Admin-Dashboard einen TCP-Call "will editieren"
  an alle in derselben Kunden-Gruppe erreichbaren Admin-fähigen Geräte. Antwort ja/nein.
  Granularität pro bearbeitetem Datensatz (Gruppe oder Alarm-Profil), nicht global — mehrere
  Admins dürfen gleichzeitig an unterschiedlichen Gruppen/Profilen arbeiten, auch parallel in
  beiden Tabs. Bei Millisekunden-Gleichstand (beide Calls kollidieren): beide lehnen sich
  gegenseitig ab, geben sofort wieder frei, Retry nach zufälligem Backoff (200–800 ms).
  Auto-Freigabe nach 10 Minuten ohne Edit-Aktivität am jeweiligen Datensatz.
- **Config-Sync ist Hot-Reload, nicht Update-Pipeline**: Konfigurationsänderungen (Raumname,
  Hotkeys, Empfängerkreise, Ton, Schwellwerte) werden bei Blur eines gültig ausgefüllten
  Pflichtfelds sofort gespeichert und per Broadcast (inkl. neuer Config-Versionsnummer) verteilt.
  Kein Save-Button. Empfangende Geräte laden die Config im laufenden Prozess neu — **kein**
  Parallelinstanz-Swap wie beim Programm-Update. Jede Config-Änderung landet zusätzlich in einer
  Änderungshistorie pro Gruppe/Alarm-Profil (letzte n Einträge, mit Undo).
- **Programm-Updates laufen ausschließlich über die Swap-Pipeline** (siehe Update-Prompt Teil 2):
  signiert, parallele Testinstanz auf separatem Testport, Freigabe erst nach lokalem Erfolg +
  mindestens einer Peer-Bestätigung, dann Port-Übernahme und Deinstallation der Altversion.
  Rollout ist gestaffelt und wird vom Admin freigegeben, nicht unkontrolliert lauffeuerartig.

## Datenschutz-Prinzipien (konkretisiert aus NFR-5 der Pflichtenheft)
- Zeige nie mehr personenbezogene Daten an als für die Alarmierung nötig: Raum + Raumnummer
  prominent, Username klein — nicht umgekehrt.
- RDP-Kennzeichnung (`SM_REMOTESESSION`) ist eine reine Sitzungs-Eigenschaft, kein
  Überwachungsfeature. Nirgends langfristig protokollieren, wer wann remote war, außer im
  regulären Alarm-Log (wer hat auf Alarm X wie reagiert), das ohnehin schon vorgesehen ist.
- Änderungshistorie und Antwort-Log enthalten Klarnamen — beides ist mit dem Auftraggeber
  bezüglich Personalrat abzustimmen (siehe offene externe Fragen), nicht eigenmächtig erweitern.

## Codestil & Sicherheit
- Kommentare auf Deutsch, sparsam, erklären das *Warum*, nicht das *Was* (Code soll fürs *Was*
  selbsterklärend sein).
- Kein Over-Engineering: keine Abstraktionsschicht, kein Interface, kein Pattern ohne konkreten,
  aktuell existierenden Anwendungsfall in diesem Dokument oder der Pflichtenheft.
  "Könnte man später brauchen" ist kein Grund.
- Neue Abhängigkeiten nur nach kurzer Nutzen-Abwägung im Kommentar an der Stelle, wo sie
  eingebunden werden (ein Satz reicht: warum diese Lib statt Eigenbau).
- Keine Secrets, Zertifikate, Passwörter, Kunden-IDs mit Klartextbezug im Code oder in Kommentaren.
- Vor jedem Abschluss eines Features: kurzer Blick, ob eine neu eingeführte Abhängigkeit bekannte
  CVEs hat — nicht als Gate, sondern als Gewohnheit. Der volle Ablauf für den Release-Zeitpunkt
  selbst liegt im Skill `dependency-check`.

## Bei Widersprüchen
Wenn eine Anforderung aus dem Task-Prompt dieser CLAUDE.md widerspricht: stoppen, nicht selbst
entscheiden, nachfragen. Das gilt besonders für die "nicht verhandelbar"-Abschnitte oben.
