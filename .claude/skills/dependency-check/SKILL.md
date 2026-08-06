---
name: dependency-check
description: Prüft NuGet-Abhängigkeiten auf bekannte CVEs vor einem Release-Build (dotnet list package --vulnerable, Dependabot-Hinweis, Platzierung in Release-Notes)
---

## Dependency-Prüfung gegen CVEs (Build-Zeit, nicht Laufzeit)

HälpMi läuft beim Kunden ohne Internet — Laufzeit-CVE-Abfragen gegen Online-Datenbanken sind daher
bewusst **kein** Bestandteil der App selbst. Die Prüfung gehört in die Entwicklungs-Pipeline:

- `dotnet list package --vulnerable --include-transitive` vor jedem Release lokal ausführen.
- Falls ein GitHub-Repo existiert: Dependabot-Alerts aktivieren (kostenlos, prüft NuGet-Pakete
  automatisch gegen die GitHub Advisory Database).
- Ergebnis der Prüfung gehört in die Release-Notes/den Commit, der einen neuen Installer baut —
  nicht in die Anwendung selbst.
