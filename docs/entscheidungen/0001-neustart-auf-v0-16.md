# 0001 – Neustart der Versionslinie auf v0.16

- Datum: 2026-08-20
- Status: angenommen
- Entschieden von: Repo-Owner

## Kontext
Der damalige `main`-Stand (zuletzt v0.39.5) war durch viele parallel statt seriell entwickelte
Features (LAN-Verschlüsselung Phase 1–3, kryptografisch verifizierte Admin-Rollen, Ed25519-
Lizenzprüfung, revisionssicheres Audit-Log, Wellen-Rollout, Update-Ei, Fast User Switching u. a.)
fragil geworden: Abstürze und ein auf rund 1 GB aufgeblähter Installer. Weitere Reparaturversuche
an diesem Stand wurden verworfen.

## Entscheidung
- Neue Hauptlinie ist der zuvor als `Vorstellungsversion-20.08.26` entwickelte, auf `v0.16.0`
  zurückgesetzte und gezielt weiterentwickelte Stand. Status „stable+buggy“: Grundfunktionen
  (Alarmversand/-empfang, Selbsttest, Config-Sync, Discovery/Boot-Call, Install-Creator/Installer)
  funktionieren weitgehend, ohne Anspruch auf Produktionsreife.
- Der alte Stand bleibt unverändert als `legacy/main-0.39.x` erhalten und wird nicht
  weiterentwickelt.
- Features aus dem Legacy-Stand werden nicht übernommen, sondern bei Bedarf einzeln über eigene
  Tickets neu entwickelt, seriell statt parallel.
- `main` wird nicht mehr automatisch bei grünen Tests aktualisiert, sondern erst nach Freigabe
  durch den Repo-Owner (siehe 0004).

## Folgen
- Dokumentation, die Legacy-Funktionen als vorhanden beschreibt, ist veraltet und wird markiert
  (z. B. `docs/architektur.md`, Abschnitt „Nicht im aktuellen Stand“).
- Die Versionsnummern v0.17–v0.39 wurden in der neuen Linie erneut vergeben; die Legacy-Tags sind
  nicht Teil dieser Linie.
