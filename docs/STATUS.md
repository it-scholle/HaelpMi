# Status

Stand: 2026-11-18

## Aktueller Stand

- Aktive Linie: `release-1.0-MVP` (GitHub-Default-Branch), Version 0.47.x. Status
  „stable+buggy“: Alarmversand/-empfang, Selbsttest, Config-Sync, Discovery/Boot-Call und
  Install-Creator/Installer funktionieren weitgehend, ohne Anspruch auf Produktionsreife.
- `main` ist der eingefrorene, getestete Stable-Anker; `legacy/main-0.39.x` ist der alte,
  nicht mehr weiterentwickelte Stand (siehe `docs/entscheidungen/0001-neustart-auf-v0-16.md`).
- Update-Pipeline (Update-Dienst, P2P-Verteilung) ist in dieser Linie inaktiv; Updates erfolgen
  manuell per Installer.
- CI (Windows-Build + Tests), gitleaks und Dependabot: in Arbeit in #145 (PR #146).
- Struktur an die MasterAgents-Vorlage angeglichen: in Arbeit in #151.

## Offene Annahmen
<!-- Jede mit Fundstelle (Datei oder Ticket). Sonst „keine“. -->

- keine `ANNAHME:`-Marker im Code.

## Bekannte Abweichungen von `AGENTS.md` Teil A

Nicht als Ausnahme freigegeben, sondern noch offen (Entscheidung durch die
Regel-Verantwortlichen, siehe PR zu #151):

- Audit-Log ohne Hash-Kette, Aufbewahrung und Schutz (`src/HaelpMi.Core/Storage/AuditLog.cs`).
- Betriebs-Log ohne Stufen, Tagesdateien, Rotation und Aufbewahrung
  (`src/HaelpMi.Core/Diagnostics/CrashLogger.cs`).
- Feste Werte im Code (z. B. Log-Laufwerk `Z:` in `SharedLogPaths.cs`, Ports in
  `AppConstants.cs`, dort bewusst fest).
- Kein Linter/Formatierer konfiguriert.

## Nächste Schritte

- #145 (CI) und #151 (Struktur) abnehmen und nach `release-1.0-MVP` mergen.
- Offene Regel-Entscheidungen aus #151 treffen (Branch-Modell, Commit-Format, Merge-Art).
- Weitere Tickets im Milestone `release-1.0-MVP`.
