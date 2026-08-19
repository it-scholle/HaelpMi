# Entwicklungsworkflow (Details zu CLAUDE.md)

Diese Datei konkretisiert die Abschnitte "Versionierung", "Tests" und "Status-Updates" aus
`CLAUDE.md`. Bei Widerspruch gilt `CLAUDE.md`.

## Versionierung

Format: `MAJOR.MINOR.PATCH`, ein einziger Strom für das ganze Produkt (kein Modul-Konzept).
Quelle der Wahrheit: `<Version>` in `Directory.Build.props`, gespiegelt in `MyAppVersion` in
`installer/HaelpMiCommon.iss.inc` — beide müssen bei jedem Bump von Hand synchron gehalten werden.

PATCH für Bugfixes/Doku/Refactoring ohne Verhaltensänderung, MINOR für neue Features oder
dringende Produktions-Fixes, MAJOR für Breaking Changes/offizielle Releases. Der Bump-Grad wird
direkt beim Commit entschieden (kein separates Commit-Typ-Präfix-System — die Versionsnummer im
Commit-Titel codiert das schon, siehe `vX.Y.Z: Beschreibung`-Stil in der bisherigen Historie).

## Branches nach main bringen: rebase, nicht merge

**Gilt für jede Session, ausnahmslos (korrigiert 15.08.2026).** Frühere Fassungen dieser Datei
machten hier noch einen Unterschied zwischen Background-/Worktree-Sessions (Branch-Pflicht) und
interaktiven Sessions im Haupt-Checkout (Direkt-Commit auf `main`) — das ist auf
Kunden-/Nutzerwunsch aufgehoben: ein direkter Commit auf `main` mitten in einer noch unfertigen
Änderung riskiert, das laufende System zu zerbomben, unabhängig davon, ob ein Mensch live im Chat
zuschaut. Jede neue Aufgabe beginnt mit `EnterWorktree` (bzw. gleichwertigem Branch-Anlegen) und
läuft bis zum fertigen, getesteten Merge auf einem eigenen Branch.

**Branch-Granularität ist die Aufgabe/das Feature, nicht der einzelne Commit.** Mehrere
Zwischen-Commits auf demselben Branch sind normal, solange die Aufgabe läuft — die Garantie
("kein Commit landet ungebrancht und ungetestet auf `main`") ist damit vollständig erfüllt, ohne
dass ein neuer Branch samt vollem Rebase-/Test-/Merge-/Push-Zyklus für jeden einzelnen Commit
nötig wäre. Ein neuer Branch beginnt erst, wenn tatsächlich eine neue, unabhängige Aufgabe startet
— nicht bei jedem Zwischen-Commit innerhalb derselben laufenden Aufgabe.

**Warum das nötig ist:** `EnterWorktree` erzeugt automatisch einen eigenen Branch + isoliertes
Arbeitsverzeichnis, aber ohne die Regel hier endet so ein fertiger Branch oft einfach liegen,
statt zurück nach `main` zu wandern — parallel dazu arbeitet eine andere Session vom selben
Vorgänger-Stand aus weiter, und der fertige Fix fehlt am Ende auf `main`. Genau das ist
am 09.08.2026 passiert: der Fix für v0.7.6 wurde in einem Worktree fertiggestellt, aber nie
zurückintegriert; `main` sprang stattdessen direkt von v0.7.5 zu einem anderen, parallelen Fix.

Grund für Rebase statt Merge: lineare, leicht lesbare Historie; jeder Commit auf `main` steht für
sich, ohne Merge-Rauschen.

### Konkreter Ablauf

1. `EnterWorktree` - legt automatisch einen neuen Branch + isoliertes Arbeitsverzeichnis an.
2. Arbeiten, committen, testen (die für den Bump-Grad passende Stufe aus `TEST-STRATEGY.md` -
   mindestens das 🔹-Smoke-Set) - alles innerhalb des Worktrees, gegen den eigenen Branch. Berührt
   ein Commit `src/HaelpMi.InstallCreator/`, `src/HaelpMi.UpdateSigner/` oder
   `Directory.Build.props` (auch ein reiner Versions-Bump zählt — das Tool zeigt seine eigene
   Version daraus an), baut der Pre-Commit-Hook (`.githooks/pre-commit`, siehe unten) die
   veröffentlichte Kopie in `tools/InstallCreator/` automatisch neu — nicht mehr auf das
   Erinnern der Session verlassen (siehe Begründung unten).
3. Branch nach jedem Commit (mindestens aber einmal je Aufgabe) mit `git push -u origin <branch>`
   nach GitHub spiegeln - Sichtbarkeit/Backup der laufenden Arbeit, nicht erst beim fertigen Merge.
4. `git rebase main` - funktioniert direkt aus dem Worktree heraus, **ohne** Umweg über
   `git fetch`/`-C`: alle Worktrees eines Repos teilen sich dieselben Refs, der Befehl liest
   `main`s aktuellen Stand einfach mit. Kein `git checkout main` nötig und in einem fremden
   Worktree auch gar nicht möglich (git verweigert das Auschecken eines Branches, der bereits in
   einem anderen Worktree ausgecheckt ist - hier: der Haupt-Checkout). Konflikte (inkl.
   Versionsnummer-Kollisionen, siehe unten) hier auflösen.
5. Nach dem Rebase **erneut testen** - ein sauberer Rebase ohne Textkonflikt ist keine Garantie,
   dass zwei für sich funktionierende Änderungen auch in Kombination funktionieren.
6. Erst wenn das grün ist: `ExitWorktree({action: "keep"})` - bringt die Session zurück in den
   Haupt-Checkout, wo `main` tatsächlich ausgecheckt ist. Kurz `git log --oneline -1`
   gegenchecken, ob `main` sich seit dem Rebase in Schritt 4 nochmal bewegt hat (parallele
   Sessions!) - falls ja, zurück zu Schritt 4 (erneut rebasen + testen).
7. `git merge --ff-only <branch>` - da der Branch gerade erst auf `main`s aktuellen Stand
   rebased wurde, ist das ein reiner Zeiger-Vorschub, es gibt nichts mehr aufzulösen.
8. Versions-Tag setzen (`vX.Y.Z`).
9. Aufräumen: `git worktree remove <pfad>`, `git branch -d <branch>`, sowie den Feature-Branch
   auch auf GitHub löschen (`git push origin --delete <branch>`) - er wurde in Schritt 3 dorthin
   gespiegelt und bleibt sonst als bereits gemergter Branch liegen.

### Install-Creator-Rebuild-Hook (seit 16.08.2026)

`tools/InstallCreator/HaelpMi.InstallCreator.exe` ist eine gitignorete, von Hand veröffentlichte
Kopie (siehe CLAUDE.md-Abschnitt "Entwickler-Tool Install-Creator"). Die dort beschriebene Regel
"nach jeder Änderung an `src/HaelpMi.InstallCreator` direkt neu bauen" wurde mehrfach von Sessions
schlicht vergessen (zuletzt v0.29.2/v0.29.3 — die veröffentlichte exe lief danach noch auf dem
Stand von v0.27.0, bis das am 16.08.2026 auffiel). Statt sich weiter auf das Erinnern einer
Prosa-Regel zu verlassen, gibt es jetzt `.githooks/pre-commit`: baut `tools/InstallCreator`
automatisch neu, sobald ein Commit Quellcode aus `src/HaelpMi.InstallCreator`,
`src/HaelpMi.UpdateSigner` oder `Directory.Build.props` (auch ein reiner Versions-Bump zählt,
seit 17.08.2026) enthält — unabhängig davon, ob Mensch oder Claude Code committet.

**Bugfix 17.08.2026:** der Hook baut dabei immer in `tools/InstallCreator/` des **Haupt-
Checkouts**, egal aus welchem `EnterWorktree`-Worktree heraus committet wurde — er ermittelt
seinen Zielpfad über den eigenen Skriptpfad (`$0`), nicht über `git rev-parse --show-toplevel`
(das hätte bei einem Commit aus einem Worktree fälschlich dessen eigene, beim Aufräumen wieder
gelöschte Kopie getroffen — genau das ist der tatsächlichen Startmenü-Verknüpfung des Nutzers
nie zugutegekommen, bis dieser Bugfix landete).

**Bugfix 19.08.2026 (Fehlerbericht "Build aus veraltetem Haupt-Checkout trotz aktuellem
main"):** der Bugfix vom 17.08.2026 hatte den `$0`-Pfad fälschlich auch für die Build-**Quelle**
verwendet, nicht nur fürs Ziel — blieb der Haupt-Checkout dadurch auf einem alten/detached Stand
hängen (`main` selbst korrekt aktuell, das Arbeitsverzeichnis des Haupt-Checkouts nicht), baute
`dotnet publish` lautlos aus dem veralteten Code, unabhängig vom tatsächlich committeten Stand.
Quelle und Ziel werden seither strikt getrennt ermittelt: die Quelle über einen frischen
`git rev-parse --show-toplevel` (liefert zuverlässig den gerade committenden Checkout, egal ob
Haupt-Checkout oder Worktree), das Ziel unverändert über `$0`. Zusätzlich verifiziert der Hook
nach dem Build per Hash- und Zeitstempel-Abgleich, dass das Artefakt wirklich aus dem geprüften
Quellstand stammt, und bricht den Commit hart ab statt still ein falsches Artefakt zu
hinterlassen, falls das je wieder auseinanderläuft. Kein Fallback über ein regelmäßiges
Synchronhalten des Haupt-Checkouts nötig — `dotnet publish` ist an keinen bestimmten
Checkout-Pfad gebunden, die Indirektion über einen separaten Quell-Checkout entfällt dadurch
komplett statt nur seltener aufzutreten.

Aktivierung einmalig pro lokalem Repository-Klon (Hooks sind nicht automatisch aktiv):

```
git config core.hooksPath .githooks
```

**Nicht pro Worktree wiederholen — `core.hooksPath` liegt in der von allen Worktrees geteilten
`.git/config`** (kein `extensions.worktreeConfig` gesetzt) und wird beim Setzen auf einen
absoluten Pfad im Haupt-Checkout aufgelöst. Ein einziges `git config core.hooksPath .githooks`,
irgendwo im Repo ausgeführt, greift danach automatisch auch in jedem künftigen `EnterWorktree`-
Worktree — verifiziert 16.08.2026 (frischer Worktree ohne jede eigene Config zeigte den Hook
sofort aktiv). Nötig ist der Befehl nur einmal pro unabhängigem Klon (z. B. auf einer neuen
Build-Maschine). Der `Start.ps1`-Selbstheilungsmechanismus (siehe CLAUDE.md) bleibt zusätzlich
bestehen und greift beim nächsten Start ohnehin, falls der Hook aus irgendeinem Grund nicht aktiv
war — zwei unabhängige Absicherungen statt einer.

### Versionsnummer-Kollisionen

Kollidieren beim Rebase zwei unabhängig vergebene Versionsnummern (z. B. weil zwei Branches
parallel denselben PATCH-Bump gewählt haben), wird neu durchnummeriert: der zuerst auf `main`
gelandete Commit behält seine Versionsnummer, der rebasete Commit rutscht auf die nächste freie
Nummer (inkl. angepasstem Git-Tag). Betrifft die Kollision einen bereits vergebenen Tag: nur den
eigenen, gerade erst (fehlgeschlagen) angelegten Tag löschen, niemals einen Tag, der schon vor dem
eigenen Rebase existierte - der gehört zu einem fremden, gültigen Commit.

Ausnahme von der ganzen Rebase-Regel: ein bereits laufender, teilweise aufgelöster Merge wird zu
Ende gebracht statt nachträglich auf Rebase umgestellt — die Regel gilt für neue Integrationen ab
jetzt, nicht rückwirkend für Arbeit, die schon mitten im Konflikt-Auflösen steckt.

### Automatisch, sobald die Tests grün sind — keine Rückfrage, kein "mach du das selbst"

Sobald die für den Bump-Grad passende Test-Stufe (siehe `TEST-STRATEGY.md`) für eine Änderung
grün ist - unabhängig davon, ob interaktiv oder als Background-Job entstanden -, wird automatisch
auf `main` gerebased und per Fast-Forward integriert. Diese Datei (zusammen mit CLAUDE.md,
Abschnitt "Versionierung") ist die
vorab erteilte Erlaubnis dafür — nicht als Hinweis, sondern als bereits erteilte, gültige
Freigabe. Konkret heißt das:

- **Keine Rückfrage vorher**, ob gemergt/gerebased werden soll — auch nicht in abgeschwächter
  Form ("bereit zum Mergen, soll ich fortfahren?").
- **Der Nutzer wird nicht gebeten, den Rebase/Merge selbst auszuführen.** "Bitte selbst
  rebasen/mergen" ist genau die Situation, die diese Regel verhindern soll — grüne Tests ersetzen
  diese Bitte vollständig.
- Es reicht ein knapper **Abschluss-Hinweis danach**: was gemergt wurde (Branch, Commit, Tag).

Einzige zwei Ausnahmen, in denen tatsächlich nachgefragt wird: Tests bleiben nach den drei
Selbstkorrektur-Versuchen rot (siehe unten), oder ein Rebase-Konflikt lässt sich nicht nach der
Regel unter "Versionsnummer-Kollisionen" auflösen. Außerhalb dieser zwei Fälle gilt die Freigabe
uneingeschränkt.

Diese automatische Freigabe deckte ursprünglich ausschließlich die lokale Integration ab:
`git rebase`, `git merge --ff-only`, Versions-Bump + Git-Tag - alles innerhalb dieses lokalen
Repos, auf diesem Rechner. **Seit 14.08.2026 gibt es ein Remote** (`origin`, siehe CLAUDE.md
Abschnitt "Versionierung") und die Freigabe schließt den Push mit ein: jeder so integrierte
Commit auf `main` wird im Anschluss automatisch nach `origin/main` gepusht, ohne Rückfrage.

Schlagen die Tests fehl, gilt die normale Eskalation: bis zu drei Selbstkorrektur-Versuche,
danach Rückfrage statt automatischer Integration eines rot laufenden Standes. Schlägt stattdessen
der Push selbst fehl (Auth-Fehler, oder `origin/main` ist inzwischen nicht mehr fast-forward),
wird nicht automatisch force-gepusht, sondern nachgefragt - lokal ist der Stand dann trotzdem
schon integriert, es geht nur um den Push selbst.

## Ablauf pro Aufgabe/Feature

1. **Vor Coding-Start:** offene Anforderungsfragen bündeln und stellen. Erst wenn alles geklärt
   ist, beginnt die Umsetzung.
2. **Während der Umsetzung:** alles Eindeutige wird ohne weitere Rückfrage umgesetzt. Rückfragen
   nur, wenn eine Entscheidung wirklich blockiert.
3. **Für jedes neue Feature:** mindestens Testfälle formulieren; im besten Fall automatisiert in
   `tests/HaelpMi.Core.Tests` (bzw. `tests/HaelpMi.Installer.Tests`, wenn es das ausführende
   System betrifft) schreiben und ausführen. `TEST-STRATEGY.md` entsprechend nachführen.
4. **Vor Abschluss einer Version:** die für den Bump-Grad geforderte Stufe aus `TEST-STRATEGY.md`
   muss grün sein (Patch → 🔹-Smoke-Set, Minor → volle coded Suite, Major → zusätzlich
   FlaUI/Audio-Loopback, sobald gebaut). Bei Fehlschlag: bis zu **3 Selbstkorrektur-Versuche**,
   danach Rückfrage statt weiter zu raten.
5. **Wird ein Test durch geänderte Anforderungen hinfällig:** nicht kommentarlos löschen -
   Grund + Datum im Test selbst vermerken (z. B. `[Fact(Skip = "...")]` mit Begründung), bevor er
   entfernt wird.
6. **Nach Abschluss:** Zyklus wiederholt sich — neue Tests für Neues, `TEST-STRATEGY.md` und
   `ALPHA-TESTPLAN.md` nachführen, geforderte Stufe grün, danach gilt die Aufgabe als fertig.

## Status-Updates in Chat-Antworten

Bei aktiver Branch-/Versions-/Git-Arbeit (nicht bei reinen Rückfragen oder Konversation ohne
Code-Bezug) wird der Stand als Tabelle mit Pipes/Dashes zusammengefasst, breite Trennlinien
oben/unten, kein Fettdruck (reiner Fließtext ist besser lesbar):

```
----------------------------------------------------------------------
| Feld            | Wert                                             |
----------------------------------------------------------------------
| Bereich         | <Feature/Bereich>                                |
| Branch          | <Branch-Name oder "-">                           |
| Version main    | <vX.Y.Z>                                         |
| Version (diese) | <vX.Y.Z bzw. "-" wenn noch offen>                |
| Status          | Klären | Implementieren | Testen | Rebasen |
|                 | Mergen | Fertig                                  |
----------------------------------------------------------------------
Zusammenfassung: ...
Offene Fragen: ... (oder "keine")
----------------------------------------------------------------------
```

Grund: `main` kann sich in diesem Repo durch parallele Background-Sessions zwischen zwei
Chat-Antworten bewegen (siehe Vorfall v0.7.6 oben) - eine feste Struktur hält fest, auf welchem
Stand eine Antwort aufbaut, ohne bei jeder Antwort neu im Fließtext danach suchen zu müssen.
