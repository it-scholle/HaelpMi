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

**Gilt für Background-/Worktree-Sessions.** Interaktive Sessions im Haupt-Checkout committen
weiterhin direkt auf `main` (siehe CLAUDE.md, Abschnitt "Versionierung") — dort ist ein Mensch
live im Chat ohnehin die Freigabe, ein Branch+Rebase-Umweg wäre Prozess ohne Gegenwert.

**Warum das für Background-Jobs nötig ist:** `EnterWorktree` erzeugt automatisch einen eigenen
Branch + isoliertes Arbeitsverzeichnis, aber ohne die Regel hier endet so ein fertiger Branch oft
einfach liegen, statt zurück nach `main` zu wandern — parallel dazu arbeitet eine andere Session
vom selben Vorgänger-Stand aus weiter, und der fertige Fix fehlt am Ende auf `main`. Genau das ist
am 09.08.2026 passiert: der Fix für v0.7.6 wurde in einem Worktree fertiggestellt, aber nie
zurückintegriert; `main` sprang stattdessen direkt von v0.7.5 zu einem anderen, parallelen Fix.

Grund für Rebase statt Merge: lineare, leicht lesbare Historie; jeder Commit auf `main` steht für
sich, ohne Merge-Rauschen.

### Konkreter Ablauf

1. `EnterWorktree` - legt automatisch einen neuen Branch + isoliertes Arbeitsverzeichnis an.
2. Arbeiten, committen, testen (die für den Bump-Grad passende Stufe aus `TEST-STRATEGY.md` -
   mindestens das 🔹-Smoke-Set) - alles innerhalb des Worktrees, gegen den eigenen Branch.
3. `git rebase main` - funktioniert direkt aus dem Worktree heraus, **ohne** Umweg über
   `git fetch`/`-C`: alle Worktrees eines Repos teilen sich dieselben Refs, der Befehl liest
   `main`s aktuellen Stand einfach mit. Kein `git checkout main` nötig und in einem fremden
   Worktree auch gar nicht möglich (git verweigert das Auschecken eines Branches, der bereits in
   einem anderen Worktree ausgecheckt ist - hier: der Haupt-Checkout). Konflikte (inkl.
   Versionsnummer-Kollisionen, siehe unten) hier auflösen.
4. Nach dem Rebase **erneut testen** - ein sauberer Rebase ohne Textkonflikt ist keine Garantie,
   dass zwei für sich funktionierende Änderungen auch in Kombination funktionieren.
5. Erst wenn das grün ist: `ExitWorktree({action: "keep"})` - bringt die Session zurück in den
   Haupt-Checkout, wo `main` tatsächlich ausgecheckt ist. Kurz `git log --oneline -1`
   gegenchecken, ob `main` sich seit dem Rebase in Schritt 3 nochmal bewegt hat (parallele
   Sessions!) - falls ja, zurück zu Schritt 3 (erneut rebasen + testen).
6. `git merge --ff-only <branch>` - da der Branch gerade erst auf `main`s aktuellen Stand
   rebased wurde, ist das ein reiner Zeiger-Vorschub, es gibt nichts mehr aufzulösen.
7. Versions-Tag setzen (`vX.Y.Z`).
8. Aufräumen: `git worktree remove <pfad>`, `git branch -d <branch>`.

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

### Automatisch, sobald die Tests grün sind

Sobald die für den Bump-Grad passende Test-Stufe (siehe `TEST-STRATEGY.md`) für eine
Background-/Worktree-Änderung grün ist, wird automatisch auf `main` gerebased und per
Fast-Forward integriert — **ohne** vorher extra nachzufragen, ob gemergt werden soll. Diese Datei
ist die vorab erteilte Erlaubnis dafür; es reicht der Abschluss-Hinweis, was gemergt wurde
(Branch, Commit, Tag), keine Bestätigung vorher.

Diese automatische Freigabe deckt ausschließlich die lokale Integration ab: `git rebase`,
`git merge --ff-only`, Versions-Bump + Git-Tag - alles innerhalb dieses lokalen Repos, auf
diesem Rechner. `git push` in ein Remote gibt es nicht.

Schlagen die Tests fehl, gilt die normale Eskalation: bis zu drei Selbstkorrektur-Versuche,
danach Rückfrage statt automatischer Integration eines rot laufenden Standes.

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
