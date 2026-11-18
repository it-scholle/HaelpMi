# AGENTS.md

Arbeitsregeln für alle KI-Agents in diesem Repo (Claude Code, Codex u. a.).
Teil A gilt projektübergreifend und kommt aus der MasterAgents-Blueprint. Teil B beschreibt dieses Projekt.

# Teil A – Allgemeine Regeln (Blueprint-Stand v1.0.0)

## Grundprinzip
- **Unklarheiten und Verbesserungsvorschläge:** Bei Unklarheiten, Annahmen, Widersprüchen oder Lücken zuerst nachfragen, nie still entscheiden. Vorschläge für sauberere Projektentwicklung (Struktur, Architektur, Tests, Sicherheit, Vorgehen) aktiv machen und vor der Umsetzung besprechen. Weichen Vorgaben oder Vorgehen von bewährten Standards ab oder sind sie fehleranfällig: freundlich, aber klar darauf hinweisen und begründen. Umgesetzt wird erst nach Freigabe.
- Das gilt auch für Dinge, nach denen niemand gefragt hat.
- Fragen in einfacher Sprache stellen, Fachbegriffe kurz erklären, eine Empfehlung nennen.

## Rangfolge und Regeländerungen
- Rangfolge: 1. AGENTS.md, 2. aktueller Auftrag bzw. Ticket, 3. übrige Doku. Bei Widerspruch stoppen und mit Verweis auf beide Stellen fragen; bis zur Klärung gilt die höhere Stelle.
- Ausnahmen nur ausdrücklich durch den Auftraggeber, nur für die aktuelle Aufgabe, im PR vermerkt. Gespeicherte Prompts und Vorlagen (z. B. „voll autonom“) heben AGENTS.md nicht auf.
- AGENTS.md enthält nur aktuell gültige Regeln (Richtwert: Teil A ≤ 200 Zeilen, Teil B so kurz wie möglich): keine Historie, keine abgeschafften Regeln. Begründungen gehören nach `docs/entscheidungen/`, Wissen nach `docs/wissen/`.
- AGENTS.md nie eigenmächtig ändern. Vorschläge gehen nur an die Regel-Verantwortlichen:
  - Auftraggeber ist regelverantwortlich: im Chat vorschlagen, nach OK umsetzen.
  - Sonst: ohne Rückfrage ein Ticket mit Label `regel-vorschlag` anlegen und den Regel-Verantwortlichen zuweisen (betroffene Regel, Problem, Formulierungsvorschlag, Anlass; vorher Duplikate prüfen). Geht das nicht: Abschnitt „Regel-Vorschlag“ in der PR- oder Commit-Nachricht mit Erwähnung der Regel-Verantwortlichen. Den Auftraggeber nur kurz informieren.
- Teil A wird im Projekt nie direkt geändert. Die Regel-Verantwortlichen entscheiden: gilt es nur hier, kommt es in Teil B unter „Ausnahmen von Teil A“; gilt es für alle, wird zuerst die Blueprint geändert und dann übernommen. Neue Blueprint-Stände werden per Ticket „Teil A auf vX.Y.Z aktualisieren“ übernommen.
- Bis zur Entscheidung gilt die bestehende Regel. Blockiert ein Widerspruch die Aufgabe eines Auftraggebers, der nicht regelverantwortlich ist:
  - Kleine und mittlere Themen: dem Auftraggeber in einfacher Sprache zur Entscheidung vorlegen (zwei Möglichkeiten, eine Empfehlung) und die Entscheidung im Regel-Ticket vermerken. Entscheiden die Regel-Verantwortlichen später anders, wird nachgearbeitet.
  - Große Themen (Architektur, Infrastruktur, neue Technologien, Datenmodell, Sicherheit, Rechte, Risiko von Datenverlust): per Ticket bzw. PR-/Commit-Nachricht an die Regel-Verantwortlichen übergeben. Nur dieser Teil der Aufgabe wartet.

## Rollen
- **Auftraggeber:** wer die Session startet. Beantwortet Rückfragen, gibt Pläne frei, gibt das OK für den Merge nach `develop`.
- **Release-Verantwortliche:** geben Releases und Hotfixes nach `main` frei.
- **Regel-Verantwortliche:** entscheiden über Änderungen an AGENTS.md.
- Zuständigkeiten nach Themen stehen in Teil B. Änderungen an einem fremden Thema brauchen die Zustimmung der zuständigen Person.

## Autonomie
- **Frei:** lesen, analysieren, Tests ausführen, auf dem eigenen Branch committen und pushen, Draft-PR öffnen.
- **Nach Freigabe des Plans:** die Umsetzung.
- **Immer vorher fragen:** Merge, Löschen, Produktivsysteme und Remote-Hosts, neue Abhängigkeiten, Architekturänderungen.

## Sessionstart und -ende
- AGENTS.md, `docs/STATUS.md` und das Ticket lesen.
- Lokal: Sind die Hooks nicht aktiv (`git config core.hooksPath` ergibt nicht `.githooks`), selbst aktivieren und kurz erwähnen. Fehlt gitleaks, melden und die Installation erklären.
- Lokal mit Claude Code: Ist in `.claude/settings.local.json` noch keine Stufe gesetzt, die Person einmal fragen, wie frei der Agent arbeiten soll: „frei“ (keine technischen Rückfragen) oder „vorsichtig“ (Rückfrage bei Shell-Befehlen); die Regeln unter „Autonomie“ gelten in beiden. Beide in einfacher Sprache erklären und den Befehl zum Selbst-Ausführen nennen: `cp .claude/stufen/<stufe>.json .claude/settings.local.json`; Einträge, die das überschreibt, vorher zeigen.
- In Cloud-Umgebungen ohne eigenen Rechner (z. B. Codex in ChatGPT) entfällt das; dort schützt die CI.
- `develop` holen und in den eigenen Branch mergen.
- Offene Dependabot-PRs melden, Sicherheits-Updates zuerst.
- Liegt `PERSOENLICH.md` im Projekt: nie committen und an das Verschieben nach `~/.claude/CLAUDE.md` erinnern.
- Am Ende der Session den Fortschritt als Kommentar ins Ticket schreiben: was erledigt ist, was offen ist, nächster Schritt.
- `docs/STATUS.md` (Projektstand, offene Annahmen, nächste Schritte) im PR mit aktualisieren, wenn sich der Projektstand durch den Merge ändert.

## Tickets
- Ablauf: Issue → sich zuweisen und `status:in-progress` → Branch aus dem Issue → PR mit `Closes #N` → Review → `status:review` → OK → der Merge schließt das Issue. Issues nie von Hand schließen. Fehlt eines der Labels `status:in-progress`, `status:review` oder `regel-vorschlag` im Repo, anlegen.
- Nur im Umfang des Tickets arbeiten; Kleinkram nur, wenn er zum Ticket gehört.
- Funde außerhalb des Tickets (nur Bugs, Sicherheit, relevante Optimierungen): kurz beschreiben und fragen, ob ein Issue angelegt wird; vorher Duplikate prüfen. Sicherheit und kritische Bugs sofort melden, den Rest gesammelt am Ende.
- Sicherheitslücken in öffentlichen Repos nie als öffentliches Issue, nur als privates Security Advisory oder im Chat.

## Review
- Vor `status:review` prüft eine frische Agent-Instanz ohne Kenntnis der Umsetzung (Subagent oder eigene Session) den Diff gegen Ticket und AGENTS.md.
- Eine Review-Session liest nur und ändert nichts; sie ist von den Regeln zu Sessionstart und paralleler Arbeit ausgenommen.
- Schwere Funde (Sicherheit, Datenverlust, Hauptfunktion unbrauchbar, Ticket nicht gelöst) werden behoben, die übrigen im PR unter „Review“ aufgeführt; der Auftraggeber entscheidet beim OK. Geht die Behebung über das Ticket hinaus oder zweifelt der Agent an einem schweren Fund, fragt er den Auftraggeber. Wurde danach mehr als eine Kleinigkeit geändert, wird erneut geprüft.
- Ist keine frische Instanz verfügbar, prüft der Agent den Diff selbst, vermerkt das im PR und weist den Auftraggeber in einfacher Sprache darauf hin.

## Branches, Commits, Merge
- `main`: nur Releases. Kein direkter Push und kein Force-Push, für niemanden (Hook `.githooks/pre-push`, bei öffentlichen Repos zusätzlich Branch-Schutz auf GitHub).
- `develop`: Integrations-Branch und GitHub-Default. Agents ändern ihn nur per PR.
- Hooks und Prüfungen nie umgehen (`--no-verify` o. Ä.). Einzige Ausnahme ist die ausdrücklich freigegebene Bereinigung der Historie.
- Ein Branch pro Ticket, abgezweigt von `develop`. Vor dem PR den aktuellen Stand von `develop` einmergen. Kein Rebase, kein Force-Push.
- Commits nach Conventional Commits: `typ: Beschreibung (#N)`, z. B. `fix: Absturz beim Export (#42)`. Keine Versionsnummer im Commit; Feature-PRs ändern die Version nicht.
- Merge durch den Agent nach OK des Auftraggebers:
  1. Aktuellen Stand von `develop` per Merge in den Branch holen.
  2. Eindeutige Konflikte lösen, sonst fragen.
  3. Tests, Lint und Build lokal und in der CI. CI-Status mit `gh pr checks`; ist das nicht verfügbar, mit `gh run list --branch <branch>`.
  4. Rot: siehe „Feststecken“. Wurde mehr als eine Kleinigkeit geändert, vor dem Merge erneut zeigen.
  5. Hat sich `develop` inzwischen bewegt: ab Schritt 1 wiederholen.
  6. Squash-Merge, danach Branch lokal und remote löschen.

## Parallele Arbeit
- Jede Session hat einen eigenen Branch und eine eigene Arbeitskopie (Worktree); nie zwei Sessions auf einem Branch.
- Wer ein Ticket bearbeitet, steht im Ticket. Fremde Tickets nicht anfassen.
- Gemeinsamen Zustand (Datenbanken, echte Daten, laufende Systeme) ändert immer nur eine Session gleichzeitig; das steht im Ticket.
- Höchstens 2 Tickets pro Person gleichzeitig in Arbeit. Hotfixes und dringende Fehler sind ausgenommen.
- Pausieren: Branch pushen, Stand und Grund ins Ticket schreiben, `status:in-progress` entfernen.

## Release und Hotfix
- SemVer `HAUPT.NEBEN.FIX`, vor dem ersten stabilen Stand `0.x`. Der Agent leitet die Version selbst aus den Commits ab (`fix` → FIX, `feat` → NEBEN, `BREAKING CHANGE` → HAUPT) und begründet sie in den Release-Notizen.
- Die Version steht an genau einer Stelle (Teil B); alles andere liest sie von dort.
- Vor dem Release `develop` → `main`: CI und Tests grün, Doku und Handbuch aktuell, offene Annahmen geklärt oder als bekannte Einschränkung aufgeführt, `CHANGELOG.md` ergänzt (Neu / Verbessert / Behoben), Version gesetzt.
- Release nach OK der Release-Verantwortlichen per normalem Merge oder Fast-Forward, nie per Squash. Danach Tag `vX.Y.Z` und GitHub-Release mit den Notizen aus dem CHANGELOG. Bei HAUPT-Sprüngen Migration und Rückweg beschreiben.
- Hotfix nur für schwere Fehler im Release (Sicherheit, Datenverlust, Hauptfunktion unbrauchbar): Branch `hotfix/…` von `main`, zuerst Regressionstest, nur die Korrektur, PR nach `main` mit OK der Release-Verantwortlichen, Squash-Merge, FIX-Sprung mit Tag und CHANGELOG. Danach sofort `main` in `develop` mergen.

## Annahmen
- Zuerst fragen. Lässt sich eine Annahme nicht vermeiden:
  - im Code mit `ANNAHME: …` markieren,
  - in `docs/STATUS.md` unter „Offene Annahmen“ führen,
  - im PR offenlegen; der Auftraggeber entscheidet beim OK.
- Vor dem Release klären oder als bekannte Einschränkung in die Release-Notizen aufnehmen.
- Geklärte Annahmen aufräumen (Marker entfernen), bei Bedarf eine Entscheidung in `docs/entscheidungen/` anlegen.

## Tests
- Jedes Feature bekommt Tests.
- Bei Bugs zuerst einen Regressionstest schreiben, der den Fehler zeigt, dann beheben.
- Tests mit Nebenwirkungen (echte Dateien, Netz, Systeme) getrennt halten und nur bewusst starten.
- Mocks und Fixtures statt echter Systeme und echter Daten.
- Tests nie abschwächen, auskommentieren oder überspringen, um grün zu werden. Veraltete Tests nur mit Begründung im PR entfernen.
- Flaky Tests reparieren, nicht ignorieren.
- Die CI führt Lint, Tests und Build mit den Befehlen aus Teil B aus. Enthält das Projekt Code, aber der Prüfteil in `.github/workflows/ci.yml` ist noch auskommentiert: melden und das Einrichten vorschlagen.

## Feststecken
- Nicht raten. Wer nicht weiterkommt, stoppt und erklärt verständlich: was versucht wurde, was beobachtet wurde, welche Vermutung besteht.

## Code
- Kommentare sparsam: das Warum, nicht das Was. Keine Daten, Namen oder Ticketnummern im Code.
- Fehler nie stillschweigend verschlucken.
- Fehlermeldungen für Nutzer nennen einen Lösungsweg: direkte Korrektur oder Link zur Stelle, an der man es beheben kann. Fehlt diese Stelle in der UI, eine schaffen.
- Keine fest eingebauten Werte (Pfade, Schwellen, URLs); alles konfigurierbar.
- Typen angeben, wo die Sprache es erlaubt.
- Formatierer und Linter sind Pflicht; Lint-Fehler blockieren den PR.
- Verständliche Namen, kleine Funktionen mit einer Aufgabe.

## Module
- Vor dem Bauen nach Vorhandenem suchen.
- Ein Modul hat eine Aufgabe, eine klare Schnittstelle und keine versteckten Abhängigkeiten.
- Nicht spekulativ erweitern; gebaut wird, was jetzt gebraucht wird.
- Gibt es ein ähnliches Modul: fragen, ob es erweitert oder bewusst ein eigenes gebaut wird.

## Design-System
- Alle UI-Elemente (Icons, Farben, Schriften, Abstände, Buttons, Badges) existieren genau einmal im Design-System-Verzeichnis (Teil B) und werden nur referenziert.
- Vorher den Katalog prüfen. Vorhandenes nicht ungefragt neu erzeugen; überarbeiten nur auf ausdrücklichen Wunsch.
- Kombinationen aus Bausteinen bauen und alle Teile ablegen, z. B. `glocke.svg` + `plus.svg` → `glocke-plus.svg`.
- Neue Icons aus der Standard-Bibliothek (Teil B) einmalig übernehmen, danach nur aus dem Verzeichnis nutzen.
- Einheitlicher Stil (Teil B). Farben nie fest im Element, sondern über das Design-System.
- Katalog führen: Name, Bedeutung, Bausteine, Quelle, Lizenz. Jedes Element hat eine Textalternative.
- Neues Element für einen Zweck, den es schon gibt: fragen, ob es das alte ersetzt (dann alle Kombinationen und Verwendungen anpassen) oder nur einmalig genutzt wird (eigener Name, Katalog-Vermerk „Ausnahme, Standard ist X“).

## UI
- Schlicht und modern, für alle Nutzer ohne Vorwissen verständlich.
- Seiten nicht überladen: Übersicht möglichst ohne Scrollen, Details in eigenen Bereichen.
- Jedes Thema hat genau eine Stelle; mehrere Wege führen zur selben Seite bzw. zum selben Modul.
- Nur Bausteine aus dem Design-System.
- Jede Aktion gibt Rückmeldung. Gefährliche Aktionen brauchen eine Bestätigung, die die Folgen klar benennt.
- Einheitliche Begriffe und Navigation.
- Barrierefreiheit ist nicht verhandelbar. Immer: Information nie nur über Farbe, ausreichender Kontrast, vollständig per Tastatur bedienbar, Textalternativen. Für Web- und App-Oberflächen gilt zusätzlich WCAG 2.2 Stufe AA.

## Geschwindigkeit
- Grundgerüst sofort anzeigen, Daten mit Platzhaltern nachladen.
- Lazy Loading, wo möglich. Absehbare nächste Schritte sparsam vorausladen; Aktionen nie vorab ausführen.
- Unabhängige Daten parallel laden.
- Rückmeldung nach Dauer: unter 0,1 s keine, ab ~1 s Ladeanzeige, ab ~10 s Fortschritt mit Restzeit und Abbrechen.
- Wichtige Daten: letzten Stand sofort anzeigen und beim Öffnen im Hintergrund aktualisieren. Werte ohne Sprung ersetzen, dezentes Aktualisieren-Symbol, bei Fehlschlag „angezeigt wird Stand von …“. Keine Relativzeit wie „vor 2 Min.“.

## Sicherheit
- Secrets nie in Code, Repo, Logs, Fehlermeldungen, UI oder Prompts.
- Secrets liegen in `.env` (gitignored, Rechte 0600); `.env.example` enthält nur die Namen.
- Secrets nie als Kommandozeilen-Argument übergeben.
- Ein geleaktes Secret gilt als kompromittiert und wird rotiert.
- Least Privilege für Programme und Zugänge. Die gemeinsamen Agent-Freigaben (`.claude/settings.json`) enthalten nur Sperren und den Guard; wie frei der Agent arbeitet, legt jede Person mit ihrer Stufe fest.
- Der Guard (`.claude/hooks/guard.sh`) ist die harte Grenze in jeder Stufe. Nie umgehen; Guard, Stufen und Settings ändert nur ein Mensch. Blockiert der Guard etwas Nötiges: Grund nennen und fragen.
- Neue Abhängigkeiten nur mit Begründung und nach CVE-Prüfung; Lockdatei committen.
- Datensparsamkeit; keine personenbezogenen Daten in Logs.
- Eingaben von außen immer validieren.
- gitleaks läuft vor jedem Commit und in der CI; Dependabot ist aktiv.
- Dependabot-PRs vor dem Merge prüfen: CI grün, Changelog der neuen Version auf inkompatible Änderungen gelesen und im PR zusammengefasst; Merge nach OK wie jeder PR. HAUPT-Versionssprünge einzeln mit eigenem Ticket.
- Meldet gitleaks einen Fund: nie selbst als Fehlalarm einstufen. Fund (maskiert) zeigen und fragen. Einträge in `.gitleaksignore` nur nach Freigabe, mit Begründung im Commit.

## Öffentliche Repos
- Was einmal öffentlich war, gilt als veröffentlicht; Löschen in einem neuen Commit reicht nicht.
- Vor jedem Commit prüfen, dass nichts Vertrauliches dabei ist (Verträge, Kunden- und Behördenbezüge, interne Dokumente, personenbezogene Daten). Im Zweifel fragen.
- Ist es passiert: sofort melden, Secrets rotieren, Bereinigung der Historie vorschlagen. Umsetzung nur nach ausdrücklicher Freigabe; das ist die einzige Ausnahme vom Force-Push-Verbot.
- Ein Repo von privat auf öffentlich umzustellen gilt als Veröffentlichung: vorher die gesamte Historie prüfen.

## Produktivsysteme und echte Daten
- Originaldaten nie verändern.
- Vor jeder Änderung einen Dry-Run vorlegen.
- Vorher sichern und den Rückweg beschreiben.
- Neues parallel zum Alten betreiben, bis es validiert ist.
- Fremde Systeme nur ergänzen, nicht umbauen.
- Jede Änderung im Audit-Format protokollieren.

## Audit-Log (Anwendungen und laufende Systeme)
- Protokolliert werden: Löschen, Rechte und Rollen, sicherheitsrelevante Einstellungen, An- und Abmeldung inkl. Fehlversuchen, Exporte. Genaue Liste in Teil B.
- Felder: Zeitpunkt, wer, was, woran (ID), vorher → nachher, Ergebnis, Modul. Bei Löschungen Metadaten, nie den Inhalt.
- Nur anhängend, mit Hash-Kette, auch über Tagesdateien hinweg; kompakt und strukturiert.
- Geschützt; Lösch- und Änderungsversuche werden selbst protokolliert.
- Aufbewahrung laut Teil B (Standard 1 Jahr). Fristlöschung wird mit dem letzten Hash protokolliert. Bei Platzmangel nie löschen, sondern als FEHLER im Log und in der UI warnen.
- Tests prüfen, dass Einträge geschrieben und Manipulationen erkannt werden.
- Änderungen von Agents an Systemen werden im selben Format protokolliert.

## Betriebs-Log
- Stufen FEHLER, WARNUNG, INFO, DEBUG (DEBUG standardmäßig aus), ohne Codeänderung einstellbar.
- Strukturiert: Zeitstempel mit Zeitzone, Stufe, Modul, Meldung, Objekt-ID, Vorgangs-ID.
- Jeden Fehler genau einmal loggen, dort wo er behandelt wird, mit Kontext und Stacktrace.
- Nie Secrets oder Nutzerinhalte loggen; maskieren.
- Meldungen an Nutzer getrennt vom Log.
- Eine Datei pro Tag und Prozess. Die aktuelle Datei hat einen festen Namen (z. B. `app.log`) und wird beim Tageswechsel oder beim Start, wenn sie Einträge eines früheren Tages enthält, in `app-JJJJ-MM-TT.log` umbenannt (Datum der Einträge).
- Aufbewahrung 90 Tage, mit Personenbezug 30 Tage; komprimieren ab 7 Tagen.
- Notbremse: ab 100 MB pro Datei WARNUNG und weiter in `app.1.log`; ab 1 GB pro Prozess die ältesten Dateien zuerst löschen und warnen. Abweichende Werte in Teil B.
- Ablageort in `docs/betrieb.md` dokumentieren.

## Doku – ein Ort pro Zweck
- `README.md`: was das Projekt ist und wie man einsteigt.
- `AGENTS.md`: gültige Regeln.
- `docs/STATUS.md`: aktueller Stand, offene Annahmen, nächste Schritte.
- `docs/entscheidungen/`: eine Datei pro Entscheidung (Datum, Kontext, Entscheidung, Folgen).
- `docs/architektur.md`, `docs/betrieb.md`, `docs/wissen/`, `CHANGELOG.md`.
- `docs/handbuch/`: Handbuch als Markdown mit Front Matter (`id`, `titel`, `kapitel`, `reihenfolge`, `zielgruppe: nutzer|admin|entwickler`, `ab_version`, `schlagworte`, `siehe_auch`). Das ist die einzige Quelle; DB oder JSON für die Anwendung werden nur daraus erzeugt, nie direkt bearbeitet. Die `id` ist fest (für kontextbezogene Hilfe) und wird nie geändert; wird eine Seite gelöscht oder aufgeteilt, bleibt ihre `id` als Weiterleitung auf die neue Stelle bestehen.
- Doku und Handbuch in jedem PR mitpflegen; vor dem Release ist das ein Pflicht-Check.
- PR-Vorlage `.github/pull_request_template.md` vollständig ausfüllen; nicht Zutreffendes als „entfällt“ markieren.
- Sprache: Deutsch, sofern Teil B nichts anderes festlegt.

# Teil B – HälpMi

## Projekt
- Zweck: LAN-Alarmierung für Windows-Verwaltungsnetze. Ein Arbeitsplatz löst per Hotkey einen Alarm aus, die zugeordneten Arbeitsplätze bekommen sofort ein erzwungenes Popup mit Ton. P2P ohne zentralen Server; eine App-interne Admin-Rolle pflegt Gruppen und Alarm-Profile.
- Sprache (Doku, UI, Commits, Code-Kommentare): Deutsch
- Sichtbarkeit des Repos: öffentlich (Lizenz source-available, siehe `LICENSE`; Abschnitt „Öffentliche Repos“ in Teil A gilt)
- Anforderungsquellen (historisch, nicht rückwirkend ändern): `docs/wissen/anforderungen/` (Pflichtenheft, Umsetzungs-Prompts Teil 1 und 2). Bei Widerspruch gilt diese Datei.

## Tech-Stack
- Sprache und Runtime: C#, .NET 8 (`net8.0-windows`), Plattform x64 (ARM64 vorgesehen)
- Frameworks: WPF; `System.Windows.Forms` nur punktuell (Tray-Icon, Zwischenablage); `System.Net.Sockets` für P2P; NAudio (Mehrgeräte-Audio); Win32-API direkt (`RegisterHotKey`, Forced-Foreground, `GetSystemMetrics(SM_REMOTESESSION)`); BouncyCastle (Ed25519)
- Datenbank und Speicher: JSON-Dateien (`System.Text.Json`) unter `%ProgramData%\HaelpMi`; Install-Creator unter `%LocalAppData%\HaelpMi-InstallCreator`
- Installer: Inno Setup (`installer/`), gebaut über das Entwickler-Tool Install-Creator
- Quelle der Versionsnummer: `<Version>` in `Directory.Build.props`, von Hand gespiegelt in `MyAppVersion` in `installer/HaelpMiCommon.iss.inc` (siehe „Ausnahmen von Teil A“)

## Befehle
| Zweck | Befehl |
|---|---|
| Einrichten | `git config core.hooksPath .githooks` |
| Tests (schnell) | `dotnet test tests/HaelpMi.Core.Tests` und `dotnet test tests/HaelpMi.InstallCreator.Tests` |
| Tests (mit Nebenwirkungen) | `dotnet test tests/HaelpMi.Installer.Tests` (verändert das System; nur auf einer Wegwerf-VM mit Adminrechten, siehe README dort) |
| Lint | noch nicht eingerichtet (offen, siehe `docs/STATUS.md`) |
| Format | noch nicht eingerichtet (offen, siehe `docs/STATUS.md`) |
| Build | `dotnet build HaelpMi.sln` (unter Linux zusätzlich `-p:EnableWindowsTargeting=true`; die Tests laufen nur unter Windows) |
| Start | Install-Creator: `tools/InstallCreator/Start.cmd`; Agent und Config über den installierten Admin-Installer |
| Abhängigkeiten prüfen | `dotnet list package --vulnerable --include-transitive` (Skill `dependency-check`) |
| Ticket-Status | `scripts/claim-issue.sh <nr>`, `scripts/finish-issue.sh <nr> ["text"]`, `scripts/release-issue.sh <nr> ["grund"]` |

## Architektur
- Überblick: `docs/architektur.md`
- Module und ihre Grenzen:
  - `HaelpMi.Core`: Logik, Netzwerk, Storage, Interop; keine UI.
  - `HaelpMi.UI`: Fenster und ViewModels; spricht nie selbst ins Netz, bekommt Dienste per Context-Objekt.
  - `HaelpMi.Agent`: einziger Prozess mit Sockets; läuft rechtelos im User-Kontext.
  - `HaelpMi.Config`: spricht nie ins Netz, nur per Named Pipe mit dem Agent.
  - `HaelpMi.InstallCreator`: Entwickler-Tool, bewusst ohne ProjectReference auf Core/UI; benötigte Kleinteile werden dort bewusst dupliziert.
  - `HaelpMi.UpdateService` (Windows-Dienst): ausschließlich Install/Test/Swap/Deinstall bei Auto-Updates, Kommunikation nur per Named Pipe (localhost), nie TCP übers Netz. Mit `HaelpMi.UpdateSigner` auf `release-1.0-MVP` inaktiv.

### Architekturprinzipien – nicht verhandelbar
- Kein zentraler Server-Prozess oder Broker. Netzwerkkommunikation ist P2P zwischen Geräten.
- Geräte-Abgleich über die persistente Geräte-GUID, nie über die IP-Adresse.
- Kein Heartbeat, kein Polling im Leerlauf. Netzwerkverkehr nur bei: eigenem Boot-Call, Alarmauslösung/-antwort, Config-Änderung, Update-Verteilung.
- Kunden-/Gruppen-ID: Jedes Admin+User-Installer-Paar trägt eine feste, im Install-Creator erzeugte ID. Alle Netzwerkpakete werden danach gefiltert; zwei Installationsgruppen im selben Netz beeinflussen sich nie.
- Die Rollen Admin und User sind App-intern (Lizenz-/Konfigurationsdatei), unabhängig von Windows-Rechten.
- „Kreis“ ist ausschließlich die ID-basierte Netz-Isolation, nie ein Dashboard-Objekt. „Gruppe“ ist die einzige organisatorische Einheit im Admin-Dashboard (Zusammenschluss von Geräten/Räumen).
- Exklusiv-Edit-Lock statt Datei-Lock: Beim Auswählen einer Gruppe oder eines Alarm-Profils geht ein TCP-Call „will editieren“ an alle erreichbaren Admin-Geräte derselben Kunden-Gruppe, Antwort ja/nein. Granularität pro Datensatz. Bei Gleichstand lehnen beide ab, geben frei und versuchen es nach zufälligem Backoff (200–800 ms) erneut. Auto-Freigabe nach 10 Minuten ohne Edit-Aktivität.
- Config-Sync ist Hot-Reload: Speichern und Broadcast (mit neuer Config-Versionsnummer) bei Blur eines gültig ausgefüllten Pflichtfelds, kein Save-Button. Empfänger laden im laufenden Prozess neu, kein Swap. Jede Änderung landet in einer Änderungshistorie pro Gruppe/Alarm-Profil (letzte n Einträge, mit Undo).
- Programm-Updates laufen ausschließlich über die Swap-Pipeline: signiert, parallele Testinstanz auf separatem Testport, Übernahme erst nach lokalem Erfolg und mindestens einer Peer-Bestätigung, danach Port-Übernahme und Deinstallation der Altversion.
- Rollout (bei aktiver Update-Pipeline): Der Admin gibt jede Version genau einmal frei, keine manuellen Stufen. Danach verbreitet sich das Update automatisch P2P; die Wellenbreite ergibt sich aus der Zahl bereits aktualisierter Peers (`UpdateOrchestrator.IsMyTurn`). Jitter vor dem Pull, Retry-Obergrenze pro Gerät, Fehlermeldung an den Admin, Not-Aus pro Kreis bei mehreren Fehlschlägen. Hintergrund: `docs/entscheidungen/0003-update-rollout.md`.
- Ladevorgänge: Fenster bzw. Prozess zuerst, Daten danach, unabhängige Vorgänge parallel (Teil A „Geschwindigkeit“; Inventar und Zielwerte: `docs/wissen/wartezeit-konzept.md`).

### Datenschutz
- Nie mehr personenbezogene Daten anzeigen als für die Alarmierung nötig: Raum und Raumnummer prominent, Username klein.
- Die RDP-Kennzeichnung (`SM_REMOTESESSION`) ist eine reine Sitzungs-Eigenschaft und wird nirgends langfristig protokolliert, außer im regulären Alarm-Log.
- Änderungshistorie und Antwort-Log enthalten Klarnamen; vor jeder Erweiterung mit dem Auftraggeber (Personalrat) abstimmen, nie eigenmächtig erweitern.

## Rollen und Zuständigkeiten
| Rolle oder Thema | Person |
|---|---|
| Release-Verantwortliche | Repo-Owner |
| Regel-Verantwortliche | Repo-Owner |
| Lizenz und rechtliche Texte | Repo-Owner (vor einem echten Release rechtskundig prüfen lassen) |

## Design-System
- Verzeichnis: `design/` (Katalog: `design/KATALOG.md`)
- Icon-Bibliothek: Lucide (ISC-Lizenz)
- Stil: hell und schlicht, ein Akzentton; Farben nur als Ressourcen aus `design/xaml/Design.xaml`; Schrift `Segoe UI Variable Text, Segoe UI`; Ecken 8 px (Karten, Buttons); Lucide-Icons im 24-px-Raster mit 2 px Linienstärke und runden Enden.

## Sicherheit und Schlüssel
- Getrennte Schlüssel, nie verwechseln oder zusammenlegen:
  1. Kunden-Lizenzsignatur (Ed25519): Soft-Expiry, kein Hard-Lock.
  2. Update-Signatur (Ed25519, eigener Schlüssel): Nur signierte Programm-Updates werden angenommen und weiterverteilt.
  3. Installer-Passwort pro Kunde (optional, kein Schlüsselpaar).
  4. Admin-Rollen-Signatur (Ed25519, pro Kunden-Gruppe): im aktuellen Stand nicht umgesetzt (nur `legacy/main-0.39.x`); bei einer Neuentwicklung pro Gruppe, nicht global.
- Nur öffentliche Schlüssel kommen ins Repo bzw. in die Binary.
- Der private Update-Signaturschlüssel liegt verschlüsselt in Vaultwarden. Der Install-Creator holt ihn per Bitwarden-CLI, hält ihn nur im Arbeitsspeicher und schreibt ihn nie auf die Platte. Fallback ohne Vaultwarden: `HaelpMi.UpdateSigner` mit lokaler Schlüsseldatei (Handbuch, Schritt 1b).
- Keine Kunden-IDs mit Klartextbezug im Code oder in Kommentaren.
- Zwischenablage: nie direkt `System.Windows.Clipboard` oder `System.Windows.Forms.Clipboard` aufrufen, immer über eine projekteigene `ClipboardCopier`-Klasse mit Retry und Rücklese-Prüfung (Vorbild `src/HaelpMi.InstallCreator/ClipboardCopier.cs`). Fehlt sie im jeweiligen Projekt, wird sie dort neu angelegt (bewusstes Duplikat, keine neue ProjectReference).
- Neue Abhängigkeit: ein Satz an der Einbindestelle, warum diese Bibliothek statt Eigenbau; CVE-Prüfung wie in Teil A.

## Code
- Kommentare, zusätzlich zu Teil A: nur, wenn der Code ohne sie wirr oder missverständlich wirkt (z. B. nicht offensichtliche Workarounds), oder um Parameter zu erklären, deren Format, Wertebereich oder Einheit sich nicht aus Name und Typ ergibt. Keine „Bugfix vom …“-Blöcke; das gehört in Commit-Message und `git blame`. Bestehende Altlasten räumt #22 auf.
- Kein Over-Engineering: keine Abstraktion, kein Interface, kein Pattern ohne aktuellen Anwendungsfall.

## Install-Creator
- `tools/InstallCreator/HaelpMi.InstallCreator.exe` ist eine gitignorierte, veröffentlichte Kopie von `src/HaelpMi.InstallCreator`. Die Git-Hooks (`pre-commit`, `post-rewrite`, `post-merge`, `post-checkout`, gemeinsame Logik in `.githooks/lib/rebuild-installcreator.sh`) bauen sie zustandsbasiert neu, sobald sich `src/HaelpMi.InstallCreator`, `src/HaelpMi.UpdateSigner`, `design/` oder `Directory.Build.props` geändert haben. Keine Session baut von Hand.
- Menschen starten das Tool über `tools/InstallCreator/Start.cmd` (bzw. `Start.ps1`), das selbst prüft und bei Bedarf neu baut; nie über die rohe `.exe`.
- Details und Vorgeschichte: `docs/wissen/installcreator-rebuild-hook.md`.

## Arbeitsablauf
- Vor dem Coding offene Anforderungsfragen gebündelt stellen; danach alles Eindeutige ohne weitere Rückfrage umsetzen und nur bei wirklich blockierenden Entscheidungen nachfragen.
- Ein Ticket = eine in sich abgeschlossene Einheit: Der Ticket-Branch landet als eigenständiger Commit im Ziel-Branch (Zwischen-Commits vorher zusammenfassen), ohne unabhängige Nebenänderungen, damit sich jeder Ticket-Commit einzeln cherry-picken oder zurücknehmen lässt.
- Branch aus dem Issue erzeugen (`gh issue develop <nr> --checkout` bzw. „Create a branch“ am Issue). Präfix nach Grad: `feature/…` (MINOR), `fix/…` (PATCH-Bugfix), `chore/…` (PATCH ohne Verhaltensänderung).
- Den Branch schon während der Arbeit nach `origin` pushen. Nach dem Merge den Branch lokal und remote löschen.
- Ticket-Status: `status:in-progress` beim Start, `status:review` bei Fertigstellung. Eine Session schließt ein Issue nie auf eigene Initiative; das tut der Merge nach OK des Auftraggebers bzw. die Session auf dessen ausdrückliche Anweisung.
- Issues mit `status:in-progress` oder `status:review` nicht anfassen. Ausnahme: Der Auftraggeber verweist ausdrücklich auf ein Issue in `status:review` und fordert Nacharbeit; dann normal übernehmen (`scripts/claim-issue.sh` entfernt `status:review`).
- Bei aktiver Branch- oder Versionsarbeit den Stand im Chat als Tabelle zusammenfassen (Bereich, Branch, Version Ziel-Branch, Version dieser Änderung, Status), ohne Fettdruck. Vorlage: `docs/wissen/git-workflow.md`.

## Test-Stufen
- Patch: Smoke-Set (die mit 🔹 markierten Tests in `docs/wissen/test-strategie.md`).
- Minor: volle coded Suite (Core, InstallCreator, Installer).
- Major/1.0+: zusätzlich FlaUI-UI-Suite und Audio-Loopback-Suite, sobald gebaut.
- Vor jedem Release zusätzlich `docs/handbuch/entwickler-alpha-testplan.md` von Hand durchgehen.
- Fehlschlag: bis zu 3 Korrekturversuche mit jeweils neuer, begründeter Vermutung, danach stoppen und berichten.
- Durch Anforderungsänderungen hinfällige Tests vor dem Entfernen mit `[Fact(Skip = "…")]` und Begründung markieren und im PR nennen.

## Logging
- Audit-Ereignisse: Alarm ausgelöst, empfangen, beantwortet (Geräte-IDs, Zeitpunkte, nie Alarmtext); Autostart-Einrichtung inkl. Fehler; Rollen- und Lizenzänderungen. Klarnamen nur im Alarm-Log (siehe Datenschutz).
- Aufbewahrung Audit-Log: 1 Jahr (im aktuellen Stand nicht umgesetzt, siehe `docs/STATUS.md`)
- Ablageort Betriebs-Log: `docs/betrieb.md`, Abschnitt „Logs“
- Abweichende Grenzen: Das Programm läuft auf fremden PCs; 20 MB / 200 MB (Repo-Owner, 2026-11-18)

## Ausnahmen von Teil A
Format: Regel – Grund – Datum – Freigabe. „Offen“ heißt: Die bisherige HälpMi-Regel gilt weiter, bis die Regel-Verantwortlichen entschieden haben (Teil A: „Bis zur Entscheidung gilt die bestehende Regel“).

- Guard und Freigabe-Stufen (`.claude/hooks/guard.sh`, `.claude/stufen/*`, Sperren in `.claude/settings.json`) werden nicht aus der Vorlage übernommen – die Befehlsgrenzen regelt die Umgebung des Repo-Owners – 2026-11-17 – Repo-Owner (Auftrag zu #151).
- `PERSOENLICH.md` wird nicht ins Projekt gelegt – persönliche Datei, gehört nach `~/.claude/CLAUDE.md` – 2026-11-18 – Repo-Owner (2026-11-18).
- Branch-Modell: kein `develop`. Pro Release-Zyklus ein Branch `release-X.Y[-Kurzname]` von `main`, der für die Dauer des Zyklus GitHub-Default ist; Ticket-PRs zielen darauf. `main` ist der getestete Stable-Anker und wird nur nach Freigabe des Repo-Owners per Fast-Forward aus dem Release-Branch aktualisiert, mit Tag – Vorgeschichte `docs/entscheidungen/0004-branch-modell-release-zyklen.md`, Vorschlag zur Angleichung `docs/entscheidungen/0010-vorschlag-branch-modell-develop-main.md` – 2026-08-24 – Repo-Owner (2026-11-18): gilt bis zum Abschluss von `release-1.0-MVP`, danach Umstellung auf `develop`/`main` nach ADR 0010.
- Commit-Format und Version: `vX.Y.Z: Beschreibung (#N)`; jeder Ticket-Commit erhöht die Version selbst (PATCH/MINOR/MAJOR nach Grad), Tag `vX.Y.Z` beim Merge. Kollidieren die Versionsnummern paralleler Branches, rückt der später gemergte auf die nächste freie Nummer (`docs/wissen/git-workflow.md`) – statt Conventional Commits mit Version erst beim Release – 2026-08-24 – Repo-Owner (2026-11-18): hängt am Branch-Modell, gilt bis zu dessen Umstellung.
- Merge-Art: Ticket-Branch per Rebase auf den Ziel-Branch und Fast-Forward bzw. PR-Merge als ein Ticket-Commit; Rebase eines bereits gepushten Ticket-Branches ist erlaubt (bisherige Praxis) – statt „kein Rebase, Squash-Merge“ – 2026-08-15 – Repo-Owner (2026-11-18): hängt am Branch-Modell, gilt bis zu dessen Umstellung.
- Versionsnummer an zwei Stellen (`Directory.Build.props` und `installer/HaelpMiCommon.iss.inc`, von Hand synchron) – Inno Setup liest die MSBuild-Version nicht automatisch – 2026-08-06 – Repo-Owner (2026-11-18): bis zur Umstellung; Ziel ist, dass ISPP die Version aus `Directory.Build.props` liest (eigenes Ticket).
- Fest eingebaute Ports und Pipe-Namen (`AppConstants.cs`) – eine Änderung erfordert ein Lockstep-Update aller Geräte, Konfigurierbarkeit wäre fehleranfälliger – Repo-Owner (2026-11-18), dauerhaft.
- Secrets: kein `.env`; die App selbst braucht keine Secrets, der private Update-Schlüssel liegt in Vaultwarden und wird nie auf die Platte geschrieben – Repo-Owner (2026-11-18), dauerhaft.
- Hook `pre-push` (Schutz von `main`, Force-Push-Sperre) aus der Vorlage nicht übernommen: Der `main`-Schutz kollidiert mit dem Branch-Modell oben. Der gitleaks-`pre-commit` ist seit #145 aktiv – Repo-Owner (2026-11-18): gilt bis zur Umstellung des Branch-Modells.
