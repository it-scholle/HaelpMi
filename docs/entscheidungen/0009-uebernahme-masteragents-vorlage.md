# 0009 – Übernahme der MasterAgents-Vorlage

- Datum: 2026-11-18
- Status: vorgeschlagen (Abnahme über den PR zu #151)
- Entschieden von: Repo-Owner (Auftrag), Umsetzung durch einen Agent

## Kontext
Die Regeln standen in einer über 500 Zeilen langen `CLAUDE.md`, gemischt mit Vorgeschichte,
Begründungen und überholten Aussagen (u. a. zu Funktionen, die nur im Legacy-Stand existieren).
Die Blueprint `it-scholle/MasterAgents` (Blueprint-Stand v1.0.0) gibt eine einheitliche Struktur
für alle Projekte vor: `AGENTS.md` mit Teil A (projektübergreifend) und Teil B (projektspezifisch),
eine Ablage pro Zweck unter `docs/`, Design-System-Verzeichnis, PR-Vorlage.

## Entscheidung
- `AGENTS.md` mit unverändertem Teil A und einem Teil B für HälpMi; `CLAUDE.md` verweist nur noch
  darauf.
- Gültige HälpMi-Regeln stehen in Teil B, Vorgeschichte und Begründungen in
  `docs/entscheidungen/`, Anleitungen und Hintergrund in `docs/wissen/`.
- Wo Teil A und die bisherigen HälpMi-Regeln sich widersprechen, gilt bis zur Entscheidung die
  bisherige Regel; die Punkte stehen in Teil B unter „Ausnahmen von Teil A“ (Status „offen“).
- Nicht übernommen: Guard und Freigabe-Stufen (Auftrag des Repo-Owners), `PERSOENLICH.md`.

## Folgen
- Teil A wird im Projekt nicht direkt geändert; neue Blueprint-Stände kommen per Ticket
  „Teil A auf vX.Y.Z aktualisieren“.
- Die offenen Ausnahmen müssen entschieden werden: übernehmen (Regel aus Teil A gilt) oder als
  dauerhafte Ausnahme freigeben.
