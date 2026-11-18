## Was und warum
<!-- Kurz in einfacher Sprache. Welches Problem wird gelöst? -->

Closes #

## Wie getestet
<!-- Welche Tests, welche Befehle, was manuell geprüft? -->

## Review
<!-- Womit geprüft (frische Agent-Instanz)? Behobene schwere Funde, übrige Funde, was nicht geprüft wurde. Keine Instanz verfügbar: hier vermerken. -->

## Risiken und Rückweg
<!-- Was kann schiefgehen? Wie wird es rückgängig gemacht? -->

## Offene Annahmen
<!-- ANNAHME-Marker aus dem Code; der Auftraggeber entscheidet beim OK. Sonst „keine“. -->

## Betroffene Zuständigkeiten
<!-- Fremde Themen laut AGENTS.md Teil B und ob die Zustimmung vorliegt. Sonst „keine“. -->

## Screenshots
<!-- Bei Änderungen an der Oberfläche. Sonst „entfällt“. -->

## Checkliste
<!-- Abhaken oder mit „entfällt“ markieren. -->
- [ ] Tests ergänzt; bei Bugs zuerst ein Regressionstest
- [ ] Lint und CI grün (Lint noch nicht eingerichtet: „entfällt“)
- [ ] Version in `Directory.Build.props` und `installer/HaelpMiCommon.iss.inc` synchron erhöht
- [ ] Branch auf aktuellem Stand des Ziel-Branches (`release-*`)
- [ ] Doku und Handbuch aktualisiert
- [ ] Keine Secrets und keine personenbezogenen Daten
- [ ] Vorhandenes wiederverwendet; neue UI-Elemente im Katalog
- [ ] Barrierefreiheit geprüft
- [ ] Audit- und Betriebs-Log berücksichtigt
- [ ] Neue Abhängigkeiten begründet und geprüft
