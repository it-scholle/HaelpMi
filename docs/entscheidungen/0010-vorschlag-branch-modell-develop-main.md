# 0010 – Vorschlag: Branch-Modell `develop`/`main` nach Teil A

- Datum: 2026-11-18
- Status: umgesetzt (2026-11-19, #194)
- Entschieden von: Repo-Owner, 2026-11-18

## Kontext
Teil A der MasterAgents-Vorlage sieht vor: `develop` als dauerhafter Integrations- und
Default-Branch, Ticket-Branches per Squash-Merge nach `develop`, `main` nur für Releases (Merge
oder Fast-Forward, Tag), Conventional Commits ohne Versionsnummer, die Version wird erst beim
Release aus den Commits abgeleitet. HälpMi arbeitet stattdessen mit rotierenden
Release-Branches und einer Versionsnummer pro Ticket-Commit (0004).

## Möglichkeiten
1. **Vorlage übernehmen:** `develop` anlegen (Start: aktueller Stand von `release-1.0-MVP`), als
   Default setzen, Ticket-PRs dorthin per Squash. Releases `develop` → `main`. Der Name des
   Release-Zyklus lebt im Milestone weiter statt im Branch-Namen.
   - Einfacher: ein fester Default-Branch, kein Umstellen pro Zyklus, `Closes #N` wirkt immer,
     keine Versionsnummer-Kollisionen zwischen parallelen Tickets mehr.
   - Schwerer: einmalige Umstellung (Default-Branch, offene PRs umhängen, Hooks/Skripte/Doku
     anpassen); Zwischenstände tragen keine eigene Versionsnummer mehr, der Install-Creator zeigt
     bis zum Release dieselbe Version.
2. **Bisheriges Modell beibehalten** und in Teil B als dauerhafte Ausnahme freigeben.

## Empfehlung
Möglichkeit 1, aber erst nach Abschluss des laufenden Zyklus `release-1.0-MVP`, damit offene PRs
nicht umgehängt werden müssen. Bis zur Entscheidung gilt das bisherige Modell.

## Folgen
Bei Annahme: Ticket „Branch-Modell auf develop/main umstellen“ anlegen; `pre-push`-Hook aus der
Vorlage übernehmen; Ausnahmen „Branch-Modell“, „Commit-Format“ und „Merge-Art“ in `AGENTS.md`
Teil B streichen.

## Umsetzung
2026-11-19 (#194): `develop` und `release-2.0` vom Endstand von `release-1.0-MVP` (v0.47.11) angelegt, `develop` ist GitHub-Default, offene PRs umgehängt, Milestone `release-1.0-MVP` geschlossen. Abweichend von Möglichkeit 1 bleibt ein Stabilisierungs-Branch `release-X.Y` zwischen `develop` und `main` (AGENTS.md Teil B, „Release-Stabilisierung über `release-X.Y`“). `pre-push` aus der Vorlage übernommen.

Abweichend von der Vorlage (Repo-Owner, 2026-11-24, #204 Punkt 12): Jeder Squash-Merge nach `develop` erhöht weiter die PATCH-Version und bekommt einen Tag `vX.Y.Z`, damit jeder Zwischenstand im Install-Creator und im E2E eindeutig erkennbar bleibt. Den Release-Tag setzt der Repo-Owner.
