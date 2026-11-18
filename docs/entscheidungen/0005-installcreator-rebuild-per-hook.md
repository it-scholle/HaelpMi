# 0005 – Install-Creator-Rebuild per Git-Hook statt Prosa-Regel

- Datum: 2026-08-16 (Hook eingeführt), 2026-08-19 (Quelle/Ziel getrennt), 2026-08-26 (zustandsbasiert)
- Status: angenommen
- Entschieden von: Repo-Owner

## Kontext
`tools/InstallCreator/HaelpMi.InstallCreator.exe` ist eine gitignorierte, veröffentlichte Kopie.
Die Regel „nach Änderungen am Install-Creator von Hand neu bauen“ wurde mehrfach vergessen
(v0.29.2/v0.29.3 liefen noch auf dem Stand v0.27.0); ein drei Tage alter Stand führte schon einmal
zu einem falschen Fehlerbericht.

## Entscheidung
- Git-Hooks bauen die Kopie automatisch neu, unabhängig von einer Session.
- Quelle (gerade committender Checkout) und Ziel (Haupt-Checkout) werden getrennt ermittelt; ein
  Hash- und Zeitstempel-Abgleich nach dem Build bricht bei Abweichung hart ab.
- Die Logik ist zustandsbasiert (Quell-Hash gegen Marker-Datei) und läuft über `pre-commit`,
  `post-rewrite`, `post-merge` und `post-checkout`, weil Rebase und Amend `pre-commit` nicht
  zuverlässig auslösen.
- `Start.cmd`/`Start.ps1` bleiben als zweite, unabhängige Absicherung für Menschen.

## Folgen
- Keine Session muss sich an manuelles Neubauen erinnern.
- Details: `docs/wissen/installcreator-rebuild-hook.md`.
