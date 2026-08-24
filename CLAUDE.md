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

## Versionslinien-Status (seit 20.08.2026 — Reset nach main-Instabilität)

Der bisherige `main`-Stand (zuletzt v0.39.5) entstand durch zu viel parallele, nie einzeln
ausreichend gehärtete Feature-Entwicklung (LAN-Verschlüsselung Phase 1–3, kryptografisch
verifizierte Admin-Rollen, echte Ed25519-Lizenzprüfung, revisionssicheres Audit-Log,
Wellen-Rollout, Update-Ei, Fast User Switching u. v. m. — alles "bleeding edge", gleichzeitig
statt seriell entwickelt) und wurde dadurch zu einem fragilen, abstürzenden, auf ~1 GB
aufgeblähten Installer. Nutzerentscheidung 20.08.2026: kompletter Neustart statt weiterer
Reparaturversuche an diesem Stand.

- **Neue Hauptversion (aktiv):** der zuvor als `Vorstellungsversion-20.08.26` entwickelte,
  auf Tag `v0.16.0` zurückgesetzte und bis `v0.16.5` gezielt weiterentwickelte Stand — jetzt
  `main`. Status **„stable+buggy"**: Grundfunktionen (Alarmversand/-empfang, Selbsttest,
  Config-Sync/fliegender Configaustausch, Discovery/Boot-Call, Install-Creator/Installer)
  funktionieren weitestgehend, mit bekannten Einschränkungen — kein Anspruch auf
  Vollständigkeit oder Produktionsreife.
- **Alte Version (deprecated, eingefroren):** der bisherige main-Stand bis v0.39.5 bleibt
  unverändert unter dem Branch `legacy/main-0.39.x` erhalten — **wird nicht
  weiterentwickelt.** Die dort enthaltenen Features (siehe oben) werden nicht übernommen,
  sondern bei Bedarf einzeln, von Grund auf, über eigene GitHub-Issues neu entwickelt —
  seriell statt parallel, um die Instabilität von vorher nicht zu wiederholen. Welche
  Features das im Detail sind und in welcher Reihenfolge: wird über die GitHub-Issues/das
  Milestone unten entschieden, nicht hier vorgeschrieben.

### Neuer Branch-/Release-Workflow

- Entwicklung findet ab sofort **ausschließlich auf separaten Branches** statt, niemals
  direkt auf `main`.
- `main` wird **nicht mehr automatisch** aktualisiert, sobald Tests grün sind — die im
  Abschnitt "Versionierung" unten beschriebene Auto-Merge-Regel ist für die Dauer dieses
  Neuaufbaus **ausdrücklich außer Kraft gesetzt**. Ein Update von `main` erfolgt erst nach
  expliziter Freigabe durch den Nutzer (er hat den fertigen Stand selbst getestet), nicht
  automatisch durch eine Session.
- Aktueller Ziel-Branch für den Neuaufbau: **`release-1.0-MVP`** (von main abgezweigt).
  Verknüpft mit dem gleichnamigen GitHub-Milestone `release-1.0-MVP` — Issues/PRs für den
  Neuaufbau werden diesem Milestone zugeordnet (GitHub kennt keine native Branch-Milestone-
  Verknüpfung; die Zuordnung läuft über die Issues/PRs, die gegen diesen Branch laufen).
- Entwicklung erfolgt ab jetzt **Issue-getrieben**: jede Aufgabe/jedes Feature bekommt zuerst
  ein GitHub-Issue (dem Milestone `release-1.0-MVP` zugeordnet), Umsetzung dann auf einem
  eigenen Branch dagegen — Issue-Erstellung selbst ist eigenständige, vom Nutzer gesteuerte
  Arbeit, keine automatische Session-Aufgabe.

## Referenzdokumente
- `pflichtenheft-lan-alarmierung.md` — Quelle der Wahrheit für die ursprünglichen FR-/NFR-Nummern.
- `claude-code-prompt-teil1.md` — erster Umsetzungs-Prompt (Grundfunktionen, P2P-Alarm, Screensaver).
- `claude-code-prompt-teil2-admin-update.md` — aktueller Umsetzungs-Prompt (Admin-Rollen,
  Config-Sync, Auto-Update). Bei Widersprüchen zwischen dieser CLAUDE.md und den Prompt-Dateien:
  CLAUDE.md gewinnt, weil sie die aktuellere, dauerhafte Regel ist.
- `docs/WORKFLOW.md` — konkretisiert die Abschnitte "Versionierung", "Tests" und "Status-Updates"
  unten (Git-Mechanik im Detail: Branch/Rebase-Ablauf, Versionsnummer-Kollisionen, Tabellenvorlage).
- `docs/WARTEZEIT-KONZEPT.md` — Analyse/Konzeptdokument (ergänzt 19.08.2026, Antwort auf die
  wiederholt aufgefallenen Wartezeiten Flaw 12/13/15/16) mit vollständigem Wartezeiten-Inventar,
  Klassifizierung und Zielwerten. Die dortige Design-Richtlinie ("Fenster/Prozess zuerst, Daten
  danach", Parallelisierung als Normalfall) gilt ab sofort für neue Lade-/Wartevorgänge.

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
- **Lizenztyp (korrigiert 09.08.2026):** Frühere Fassungen dieser Datei sowie
  `Anweisungen/pflichtenheft-HälpMi.md` (Zeile 24, FR-31) legten die Lizenz konkret auf **MIT**
  fest. Das war ein veralteter Stand — die tatsächliche Vorgabe der Stadt als Auftraggeberin ist
  allgemeiner: **Quellcode muss öffentlich einsehbar/"open source" sein**, ohne dass MIT
  namentlich verlangt wird. Damit ist auch eine **Source-Available-Lizenz** (öffentlicher
  Quellcode, aber keine freie kommerzielle Weiterverwertung durch Dritte — z. B. Business Source
  License, Functional Source License oder Elastic License v2, siehe Preis-/Lizenzmodell-Notiz)
  eine offene, noch nicht final entschiedene Option. **Wichtig:** "Open Source" im engeren
  (OSI-)Sinn verbietet Einschränkungen der kommerziellen Nutzung durch Dritte per Definition —
  falls die Stadt den Begriff im engeren Sinn vertraglich verwendet, muss das vor einer
  Source-Available-Entscheidung schriftlich mit ihr geklärt werden, bevor die `LICENSE`-Datei
  geändert wird. Bis dahin bleibt die im Repository liegende `LICENSE`-Datei unverändert MIT.
  `pflichtenheft-HälpMi.md` selbst wird nicht rückwirkend editiert (historische FR-Quelle), diese
  CLAUDE.md-Notiz gewinnt bei Widersprüchen wie in den Referenzdokumenten oben beschrieben.
- **Umstellung auf Source-Available als Entwurf (ergänzt 17.08.2026):** Die oben verlangte
  schriftliche Klärung mit der Stadt liegt weiterhin nicht vor — die Klärung erfolgte bislang nur
  **telefonisch mit der städtischen IT** (17.08.2026). Auf ausdrücklichen Wunsch des Nutzers wird
  die `LICENSE`-Datei trotzdem bereits jetzt auf das oben beschriebene Source-Available-Modell
  umgestellt (öffentlicher Quellcode, kostenfrei für Privatpersonen, kostenpflichtige
  Nutzungsvereinbarung für Organisationen, keine Weiterverwertung durch Dritte zu
  Konkurrenzprodukten) — bewusst als vorläufige Schutzmaßnahme, um die im bisherigen MIT-Text
  erlaubte freie kommerzielle Weiterverwertung durch Dritte nicht länger offenzulassen, während die
  schriftliche Bestätigung noch aussteht. `LICENSE` und der Lizenz-Abschnitt in `README.md` tragen
  dazu einen deutlichen Entwurfs-Hinweis. **Vor einem echten Release weiterhin offen:** schriftliche
  Bestätigung der Stadt nachholen und den Lizenztext von einer rechtskundigen Person prüfen lassen.
- `.gitignore` gegen Secret-Dateimuster von Anfang an.
- Vier getrennte kryptografische Schlüsselpaare, niemals verwechseln oder zusammenlegen:
  1. Kunden-Lizenzsignatur (Ed25519) — Soft-Expiry, kein Hard-Lock.
  2. Update-Signatur (Ed25519, **separater** Schlüssel) — nur signierte Programm-Updates werden
     von einem Client angenommen und weiterverteilt.
  3. Optional pro Kunde: Installer-Passwort (kein Schlüsselpaar, siehe Install-Creator).
  4. **Admin-Rollen-Signatur (Ed25519, seit 17.08.2026)** — **pro Kunden-Gruppe**, nicht
     global (bewusster Unterschied zu Schlüssel 2): Install-Creator erzeugt sie bei jeder
     Neuinstallation, direkt neben der `CustomerGroupId`. Öffentlicher Schlüssel in beiden
     Installer-Varianten, privater nur im Admin-Installer. Macht `Role.Admin` in
     Boot-Call-Nachrichten kryptografisch nachprüfbar (`AdminRoleSigner`/`AdminRoleVerifier`
     in `HaelpMi.Core/Security`, `DeviceEntry.AdminVerified`) statt einer reinen
     unauthentifizierten Selbstauskunft — Grundlage für die Antwort auf EditLock-Anfragen,
     die Herkunftsprüfung bei Config-Sync und die Audit-Sync-Push-Ziele/Digest-Antworten.
     Migrationspfad für bereits installierte Admin-Geräte (`deployment.json` wird nie vom
     laufenden Programm neu geschrieben, ein reines Programm-Update liefert diesen
     Schlüssel also nicht automatisch nach): `AdminRoleTrustStore` erzeugt auf einem
     Admin-Gerät ohne jeden Schlüssel selbst eines und verbreitet es danach automatisch
     P2P an andere Admin-Geräte derselben Kundengruppe (`AdminRoleKeySyncService`, hängt am
     ohnehin stattfindenden Boot-Call-Kontakt) — kein manueller Bootstrapper-Lauf pro
     Bestandsgerät nötig, der private Schlüssel verlässt ein Gerät dabei ausschließlich
     über einen SecureEnvelope-verschlüsselten Kanal. Restrisiko bewusst in Kauf genommen:
     die allererste Schlüssel-Verbreitung in einem noch komplett schlüssellosen Kreis ist
     Trust-on-First-Use, dieselbe Grenze, die für die Geräte-Identität (fünftes
     Schlüsselpaar unten) bereits akzeptiert ist.
- Nur die jeweiligen **öffentlichen** Schlüssel werden ins Repository/die Binary eingebettet.
  Private Schlüssel gehören nie ins Repo, nie ins Log, nie in eine Fehlermeldung.
- **Aufbewahrung des privaten Update-Signaturschlüssels (seit 13.08.2026):** liegt verschlüsselt
  in Vaultwarden (Secure Note `HälpMi-Update-PrivateKey`), nicht als Klartextdatei auf einer
  Build-Maschine. `HaelpMi.InstallCreator` (Knopf "Update erstellen", siehe "Self-Bootstrap-
  Update" weiter unten) holt ihn dort bei Bedarf per
  Bitwarden-CLI ab (Master-Passwort einmal pro
  Programmstart), hält ihn ausschließlich im Arbeitsspeicher dieses einen Laufs und schreibt ihn
  nie auf die Platte. Der bisherige rein manuelle Weg über `HaelpMi.UpdateSigner` (Schlüssel als
  lokale `.txt`-Datei) bleibt als Fallback für Maschinen ohne Vaultwarden-Zugriff bestehen, siehe
  `BUILD-UND-INSTALLATION.md` Schritt 1b.

## Entwickler-Tool Install-Creator — Rebuild läuft automatisiert (Hook, seit 16.08.2026)
- `tools/InstallCreator/HaelpMi.InstallCreator.exe` ist eine gitignorete, von Hand veröffentlichte
  Kopie von `src/HaelpMi.InstallCreator`. Der Git-Hook `.githooks/pre-commit` baut sie automatisch
  neu, sobald ein Commit `src/HaelpMi.InstallCreator`, `src/HaelpMi.UpdateSigner` oder
  `Directory.Build.props` berührt (letzteres zählt mit, weil das Tool seine eigene
  Versionsnummer im Fenstertitel aus genau dieser Datei zur Build-Zeit anzeigt — ein reiner
  Versions-Bump ohne sonstige InstallCreator-Änderung ließ die angezeigte Version sonst
  veraltet stehen, Nutzerkorrektur 17.08.2026) — läuft als reiner Git-Subprozess
  (`dotnet publish`), unabhängig von einer Claude-Code-Sitzung und ohne deren Tokens zu
  verbrauchen. Details, inkl. einmaliger Aktivierung
  (`git config core.hooksPath .githooks` — gilt repo-weit für alle Worktrees, nicht nur den
  einen Checkout, in dem der Befehl lief): `docs/WORKFLOW.md` Abschnitt
  "Install-Creator-Rebuild-Hook".
- **Bugfix 19.08.2026:** der Hook ermittelt Build-Quelle (`src/HaelpMi.InstallCreator/...`) und
  Build-Ziel (`tools/InstallCreator/`) seit diesem Datum über zwei getrennte Pfade statt einer
  gemeinsamen Variable - vorher konnte ein auf altem/detached Stand hängengebliebener
  Haupt-Checkout dazu führen, dass lautlos aus veraltetem Code gebaut wurde, obwohl der
  tatsächlich committete Stand (Worktree oder main) längst aktuell war. Quelle wird jetzt immer
  frisch über `git rev-parse --show-toplevel` (den gerade committenden Checkout) ermittelt, Ziel
  bleibt wie zuvor über den eigenen Hook-Skriptpfad am Haupt-Checkout. Eine Verifikation nach dem
  Build (Hash- und Zeitstempel-Abgleich) bricht den Commit hart ab, statt je wieder still ein
  falsches Artefakt zu erzeugen. Details: `docs/WORKFLOW.md` Abschnitt
  "Install-Creator-Rebuild-Hook".
- **Frühere Fassung dieses Abschnitts (bis 16.08.2026) verlangte zusätzlich, dass jede
  Claude-Code-Sitzung nach eigenen Änderungen an `src/HaelpMi.InstallCreator` manuell neu baut —
  das ist mit dem Hook entfallen, keine Session muss sich das mehr merken.** Grund für die
  Streichung: genau diese Prosa-Regel wurde in der Praxis mehrfach vergessen (v0.29.2/v0.29.3 —
  die veröffentlichte exe lief danach noch auf dem Stand von v0.27.0), ein bei jedem Commit
  automatisch greifender Hook ist zuverlässiger als das Erinnern einer Sitzung.
- **Für den Menschen bleibt unverändert `tools/InstallCreator/Start.cmd` (bzw. `Start.ps1`)** der
  vorgesehene Weg: vergleicht Quell- und Exe-Zeitstempel selbst und baut bei Bedarf automatisch
  neu, bevor das Tool startet — zweite, vom Hook unabhängige Absicherung, greift z. B. falls der
  Hook aus irgendeinem Grund nicht aktiv war. Der rohe Aufruf von `HaelpMi.InstallCreator.exe`
  prüft das nicht und bleibt potenziell veraltet — bei Verdacht auf einen veralteten Stand zuerst
  prüfen, ob die Desktop-Verknüpfung noch auf die rohe `.exe` statt auf `Start.cmd` zeigt.
- Grund für den ursprünglichen Rebuild-Zwang überhaupt: ein 3 Tage alter, still veralteter Stand
  von `tools/InstallCreator.exe` hat schon einmal zu einem falschen Fehlerbericht geführt
  (fehlende Icon-Buttons, die auf `main` längst gefixt waren).

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
- **Rollout-Freigabe (korrigiert 11.08.2026)**: Abschnitt 11 im Update-Prompt Teil 2 beschreibt
  noch ein gestaffeltes Freigabekontingent (Admin gibt Stufe für Stufe frei, z. B. 1 → 2 → 4 → 8
  Geräte) — das ist überholt und **nicht** mehr die gültige Regel, CLAUDE.md gewinnt hier wie im
  Referenzdokumente-Abschnitt festgelegt. Tatsächlich gibt der Admin das Update **genau einmal**
  frei. Ab da verbreitet sich das Update vollautomatisch von Gerät zu Gerät weiter: jedes Gerät,
  das das Update selbst erfolgreich übernommen hat, gibt es beim eigenen nächsten Boot-Call an
  Peers weiter (wie in Abschnitt 11 Schritt 5 ohnehin beschrieben) — ohne Admin-gesteuerte
  Zwischenstufen, ohne manuell hochzusetzendes Kontingent. Die restlichen Sicherungen aus
  Abschnitt 11 bleiben unverändert gültig: lokaler Testping + mindestens eine Peer-Bestätigung vor
  jeder Übernahme, Jitter vor dem Update-Pull, Retry-Obergrenze pro Gerät, Fehlermeldung an den
  Admin. Der dortige Not-Aus-Mechanismus ("bei mehreren Fehlschlägen stoppt die gesamte
  Verteilung") gilt jetzt pro Kreis statt pro Stufe, da es keine Stufen mehr gibt.
- **Wellen-Rollout (ergänzt 16.08.2026):** ergänzt die Korrektur oben, hebt sie nicht auf — nach
  wie vor genau eine Admin-Freigabe pro Version, nach wie vor kein manuell gestuftes Kontingent
  (der Admin klickt nie eine Stufe hoch, es gibt keine Stufen-UI mehr im Dashboard). Die erlaubte
  Wellenbreite ergibt sich aber automatisch statt "sofort alle auf einen Schlag": ein Gerät darf
  erst aktualisieren, wenn mindestens so viele Peers laut eigenem, zwangsläufig unvollständigem
  P2P-Wissen (`DeviceEntry.LastKnownProgramVersion`, aus Boot-Call + Gossip) die freigegebene
  Version schon haben, wie es selbst in der deterministischen Warteschlange vor sich hat — siehe
  `UpdateOrchestrator.IsMyTurn`. Kein Admin-Eingriff pro Welle, kein zentraler Zähler (bleibt
  P2P). Grund: ein einziger Schlag auf die gesamte Kundengruppe birgt bei einer fehlerhaften
  Version ein größeres Blast-Radius-Risiko als eine automatisch anwachsende Welle; der
  bestehende Kill-Switch (pro Kreis, siehe oben) bleibt zusätzlich unverändert bestehen.
- **Self-Bootstrap-Update (ergänzt 16.08.2026):** löst die Lücke, dass die allererste Maschine
  einer Kundengruppe bisher nur per komplett neuem Installer-Lauf auf eine neue Version kam — das
  Wellen-Gate oben setzt zwingend mindestens einen bereits aktualisierten Peer voraus und kann
  daher nie die allererste Maschine selbst bedienen. Die frühere "Update-Ei"-Checkbox in
  `HaelpMi.InstallCreator` ist entfernt: jeder Admin-Installer-Build bettet das P2P-Startpaket
  jetzt automatisch ein, sofern ein Update-Schlüssel geladen ist (kein Nachteil, wenn es immer
  dabei ist). Neuer Knopf "Update erstellen" baut zusätzlich eine einzelne, eigenständig
  lauffähige Datei (`HaelpMi.UpdateBootstrapper` mit angehängtem signiertem Paket, siehe
  `EmbeddedPackage.cs`/`UpdatePackageBuilder.AppendUpdatePackage` für das Dateiformat). Ein Admin
  führt sie einmalig per Doppelklick auf einer bereits installierten Maschine aus - diese
  Maschine aktualisiert sich sofort selbst (Install/Test/Swap gegen den lokalen
  `HaelpMi.UpdateService`, ohne auf einen Peer-Boot-Call zu warten - der bei der allerersten
  Maschine ja noch niemanden gäbe) und wird danach im "Updates"-Tab des Admin-Dashboards zur
  Freigabe sichtbar. Ab der Freigabe gilt wieder unverändert die normale P2P-Wellen-Kaskade oben.
  Bewusst noch NICHT umgesetzt (Nutzerwunsch, für später vorgemerkt): ein Auto-Publish, das das
  fertige Update-Paket automatisch per hinterlegter Mail an alle hinterlegten Kunden verschickt -
  die Build-Logik (`HaelpMi.InstallCreator.MainWindow.BuildUpdateBootstrapperAsync`) ist bewusst
  von der UI getrennt und liefert schon den fertigen Dateipfad zurück, damit ein künftiger
  Versand-Schritt sie direkt aufrufen kann, ohne einen Button-Klick zu simulieren - Kundenregister
  und Mailversand selbst existieren noch nicht und sind eigenständig zu klären, wenn es soweit ist.

## Datenschutz-Prinzipien (konkretisiert aus NFR-5 der Pflichtenheft)
- Zeige nie mehr personenbezogene Daten an als für die Alarmierung nötig: Raum + Raumnummer
  prominent, Username klein — nicht umgekehrt.
- RDP-Kennzeichnung (`SM_REMOTESESSION`) ist eine reine Sitzungs-Eigenschaft, kein
  Überwachungsfeature. Nirgends langfristig protokollieren, wer wann remote war, außer im
  regulären Alarm-Log (wer hat auf Alarm X wie reagiert), das ohnehin schon vorgesehen ist.
- Änderungshistorie und Antwort-Log enthalten Klarnamen — beides ist mit dem Auftraggeber
  bezüglich Personalrat abzustimmen (siehe offene externe Fragen), nicht eigenmächtig erweitern.

## Codestil & Sicherheit
- **Kommentarregel (verschärft 24.08.2026, Nutzerkorrektur nach v0.16.6):** Kommentare auf
  Deutsch, so sparsam wie möglich. Ein Kommentar ist nur gerechtfertigt, wenn der Code ohne ihn
  vermutlich wirr/unsinnig wirkt oder falsch verstanden werden könnte (z. B. eine nicht
  offensichtliche Workaround-Entscheidung), oder wenn er Parameter erklärt, deren
  Bedeutung/erwarteter bzw. gewollter Inhalt (Format, gültiger Wertebereich, Einheit) sich nicht
  schon aus Name/Typ ergibt. Explizit **kein** Kommentar für: (a) das reine *Was* — was der Code
  ohnehin schon selbsterklärend sagt, nicht in Prosa wiederholen; (b) Änderungs-/
  Commit-Dokumentation — Datum, Ticketnummer, "Bugfix vom …", wer wann was geändert hat, gehört
  in die Commit-Message/`git blame`, nicht in den Code. Grund: genau dieses Muster (lange, mit
  Datum/Ticket versehene "Bugfix …"-Kommentarblöcke, die Commit-Historie im Code duplizieren)
  hat sich im bisherigen Code eingeschlichen — Aufräumen dafür ist als GitHub-Issue #22
  (Milestone `release-2.0-Logging/ecnrypted-communication`) vorgemerkt, nicht rückwirkend
  Teil dieser CLAUDE.md-Änderung.
- Kein Over-Engineering: keine Abstraktionsschicht, kein Interface, kein Pattern ohne konkreten,
  aktuell existierenden Anwendungsfall in diesem Dokument oder der Pflichtenheft.
  "Könnte man später brauchen" ist kein Grund.
- Neue Abhängigkeiten nur nach kurzer Nutzen-Abwägung im Kommentar an der Stelle, wo sie
  eingebunden werden (ein Satz reicht: warum diese Lib statt Eigenbau).
- Keine Secrets, Zertifikate, Passwörter, Kunden-IDs mit Klartextbezug im Code oder in Kommentaren.
- Vor jedem Abschluss eines Features: kurzer Blick, ob eine neu eingeführte Abhängigkeit bekannte
  CVEs hat — nicht als Gate, sondern als Gewohnheit. Der volle Ablauf für den Release-Zeitpunkt
  selbst liegt im Skill `dependency-check`.

## Versionierung
`MAJOR.MINOR.PATCH` in einem einzigen Strom — ein Produkt, keine separat versionierten Module.
`<Version>` in `Directory.Build.props` und `MyAppVersion` in `installer/HaelpMiCommon.iss.inc`
müssen bei jedem Versionssprung von Hand synchron gehalten werden (Kommentar an beiden Stellen
verweist aufeinander). PATCH für Bugfixes/Doku/Refactoring ohne Verhaltensänderung, MINOR für
neue Features oder dringende Produktions-Fixes, MAJOR für Breaking Changes/offizielle Releases —
direkt beim Commit entschieden (kein Typ-Präfix-System nötig, die Versionsnummer selbst codiert
den Bump-Grad schon). Commit-Message-Stil: `vX.Y.Z: Beschreibung` (siehe bisherige Historie). Bei
jedem Bump einen Git-Tag `vX.Y.Z` setzen.

**Branch-Pflicht für alle Sessions, ausnahmslos (korrigiert 15.08.2026):** Frühere Fassungen
dieses Abschnitts machten hier noch einen Unterschied — Background-/Worktree-Sessions auf
eigenem Branch, interaktive Sessions im Haupt-Checkout direkt auf `main`. Das ist auf
Kunden-/Nutzerwunsch aufgehoben: ein direkter Commit auf `main` mitten in einer noch unfertigen
Änderung riskiert, das laufende System zu zerbomben, egal ob interaktiv oder im Hintergrund
gearbeitet wird. Gültige Regel ab jetzt: **jede** neue Aufgabe — interaktiv wie Background —
beginnt mit einem eigenen Branch (`EnterWorktree` bzw. gleichwertiges Branch-Anlegen), niemals ein
Commit direkt auf `main`. Granularität ist die Aufgabe/das Feature, nicht der einzelne Commit —
mehrere Zwischen-Commits auf demselben Branch sind normal, solange die Aufgabe läuft; ein neuer
Branch pro literalem `git commit` wäre unnötiger Overhead ohne zusätzlichen Sicherheitsgewinn,
solange kein Commit ungebrancht auf `main` landet.

Branch-Namenskonvention: Präfix nach Bump-Grad + kebab-case-Kurzbeschreibung —
`feature/<kurzbeschreibung>` (MINOR), `fix/<kurzbeschreibung>` (PATCH-Bugfix),
`chore/<kurzbeschreibung>` (PATCH ohne Verhaltensänderung, z. B. Doku/Refactoring). Beispiel:
`feature/multi-vlan-bridge-seed`.

**Ausgesetzt seit 20.08.2026 für die Dauer des Neuaufbaus (siehe "Versionslinien-Status"
oben):** die folgende Auto-Merge-Regel gilt vorerst **nicht** — main-Updates brauchen bis auf
Weiteres explizite Nutzer-Freigabe statt eines automatischen Merges bei grünen Tests. Der
Rest des Ablaufs (Branch-Pflicht, Rebase-Mechanik, Tag-Setzen) bleibt unverändert gültig.

**Alle Sessions führen Rebase + `merge --ff-only` nach `main` selbst aus, sobald die passende
Test-Stufe grün ist — ausnahmslos, ohne Rückfrage, ohne Bestätigungsschritt.** Diese Zeile *ist*
die Freigabe, ein für alle Mal erteilt, nicht nur "im Prinzip". Frag nicht nach, ob gemergt werden
soll, formuliere keine Bestätigungsfrage dazu ("soll ich mergen?", "bereit zum Rebase?" o. ä.), und
sag dem Nutzer nicht, er solle es selbst per Hand tun — das grüne Testergebnis *ist* die
Zustimmung, die sonst vom Nutzer käme. Einzige Ausnahmen: Tests bleiben nach den in
`docs/WORKFLOW.md` vorgesehenen Selbstkorrektur-Versuchen rot, oder ein Rebase-Konflikt lässt sich
nicht regelbasiert auflösen (siehe Versionsnummer-Kollisionen in `docs/WORKFLOW.md`) — dann und nur
dann nachfragen. Danach reicht ein knapper Abschluss-Hinweis (was gemergt wurde: Branch, Commit,
Tag), keine Vorab-Bestätigung. Details: `docs/WORKFLOW.md`.
Grund für die Pflicht hier: genau das Fehlen dieses Ablaufs hat dazu geführt, dass ein per
Background-Job fertiggestellter Fix (v0.7.6, Branch nie zurückgeführt) auf `main` schlicht
gefehlt hat, während eine andere Session parallel auf demselben Vorgänger-Stand weitergearbeitet hat.
Zusätzlicher, wiederholt aufgetretener Fehler: Sessions haben trotz dieser Regel den Nutzer gefragt
oder gebeten, selbst zu mergen/rebasen — das widerspricht der hier erteilten Freigabe und soll nicht
mehr vorkommen.

**Remote (seit 14.08.2026):** Es gibt ein GitHub-Remote (`origin` →
https://github.com/it-scholle/HaelpMi, HTTPS-Auth über Windows Credential Manager/PAT). Gültige
Regel: **jeder Commit auf `main`** — ausnahmslos per Rebase + `merge --ff-only` aus einem
Feature-Branch nachgezogen, siehe oben — **wird im Anschluss automatisch auch nach `origin/main`
gepusht**, ohne Rückfrage. Gleiche Freigabe-Logik wie beim lokalen Merge oben: das grüne
Testergebnis, das den Merge auf `main` erlaubt, erlaubt auch den Push. Ausnahmen wie dort
(Tests bleiben rot, Konflikt nicht regelbasiert lösbar) gelten sinngemäß auch fürs Pushen, plus
zusätzlich: schlägt der Push selbst fehl (z. B. Auth, `rejected`/nicht fast-forward, weil
`origin/main` inzwischen abweicht), wird nicht automatisch force-gepusht — dann nachfragen statt
zu raten, aus demselben Grund wie bei Rebase-Konflikten.

Zusätzlich seit 15.08.2026: der Feature-Branch selbst wird während der Arbeit nach `origin`
gepusht (nicht erst beim fertigen Merge) — Sichtbarkeit/Backup auf GitHub schon während der
Umsetzung, nicht erst am Ende. Nach erfolgreichem FF-Merge auf `main` wird der Branch lokal
**und** remote gelöscht (`git branch -d` + `git push origin --delete <branch>`), damit auf
GitHub keine bereits gemergten Branches liegen bleiben.

## Status-Updates
Bei aktiver Branch-/Versions-/Git-Arbeit wird der Stand als Pipe/Dash-Tabelle zusammengefasst
(Bereich, Branch, Version main, Version dieser Änderung, Status), kein Fettdruck. Details, inkl.
Vorlage: `docs/WORKFLOW.md`.

## Anforderungen klären, dann durcharbeiten
- Vor Coding-Start: offene Anforderungsfragen gebündelt stellen. Erst wenn alles geklärt ist,
  beginnt die Umsetzung.
- Während der Umsetzung: alles Eindeutige ohne weitere Rückfrage umsetzen. Rückfragen nur, wenn
  eine Entscheidung wirklich blockiert – dann klären und weiterarbeiten, nicht auf einen
  Sammel-Termin warten.

## Tests
- Aktive automatisierte Suite: `tests/HaelpMi.Core.Tests` (xUnit, läuft bei jedem `dotnet test`,
  keine Systemänderung), `tests/HaelpMi.Installer.Tests` (xUnit, **verändert das ausführende
  System** — Program Files, ProgramData, Dienste, Registry, Firewall-Regeln, läuft nicht
  automatisch mit) und `tests/HaelpMi.Audio.Tests` (xUnit, **spielt hörbar den echten Alarmton
  auf jedem aktiven Wiedergabegerät ab** — seit 13.08.2026 aus `HaelpMi.Core.Tests` ausgelagert,
  nachdem genau das unregelmäßig und ungefragt auf einer Session-VM piepte; läuft ebenfalls
  nicht automatisch mit). `TEST-STRATEGY.md` ist die Quelle der Wahrheit für Umfang und Gates,
  `ALPHA-TESTPLAN.md` deckt das (noch) nicht Automatisierte manuell ab.
- Jedes neue Feature bekommt mindestens formulierte Testfälle, im besten Fall geschriebene und
  ausgeführte automatisierte Tests.
- Versions-Gates (aus `TEST-STRATEGY.md`): Patch → 🔹-Smoke-Set, Minor → volle coded Suite
  (Core + Installer), Major/1.0+ → zusätzlich FlaUI-UI-Suite + Audio-Loopback-Suite, sobald gebaut.
- Vor Abschluss einer Version muss die für den jeweiligen Bump-Grad geforderte Stufe grün sein.
  Bei Fehlschlag: bis zu 3 Selbstkorrektur-Versuche, danach nachfragen statt weiter raten.
- Durch Anforderungsänderungen hinfällige Tests werden nicht kommentarlos gelöscht: Grund + Datum
  im Test selbst vermerken (z. B. `[Fact(Skip = "...")]` mit Begründung), bevor sie entfernt werden.

## Bei Widersprüchen
Wenn eine Anforderung aus dem Task-Prompt dieser CLAUDE.md widerspricht: stoppen, nicht selbst
entscheiden, nachfragen. Das gilt besonders für die "nicht verhandelbar"-Abschnitte oben.
