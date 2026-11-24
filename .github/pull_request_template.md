## Was und warum
<!-- Kurz in einfacher Sprache. Welches Problem wird gelöst? -->

Closes #

## Wie getestet
<!-- Welche Tests, welche Befehle, was manuell geprüft? -->

## E2E
<!-- Nach AGENTS.md Teil B „E2E-Tests“. Nicht E2E-relevant: „entfällt“ mit Begründung. -->
- Tests ergänzt oder angepasst: <!-- T-Nummern und Bereich; angepasste Tests mit Grund -->
- Lauf: <!-- URL des Laufs (Label `e2e`), Filter, ggf. `produkt_ref`; kein Lauf möglich: „ausstehend“ mit Grund -->
- Ergebnis: <!-- grün / rot (welche Tests) / ausstehend -->
- Produktbefunde: <!-- gemeldete Issue-Vorschläge, sonst „keine“ -->

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
- [ ] E2E nach AGENTS.md „E2E-Tests“: Tests ergänzt oder angepasst, Lauf ausgewertet (Abschnitt „E2E“ ausgefüllt)
- [ ] Lint und CI grün (Lint noch nicht eingerichtet: „entfällt“)
- [ ] Version in `Directory.Build.props` erhöht
- [ ] Branch auf aktuellem Stand des Ziel-Branches (`release-*`)
- [ ] Doku und Handbuch aktualisiert
- [ ] Keine Secrets und keine personenbezogenen Daten
- [ ] Vorhandenes wiederverwendet; neue UI-Elemente im Katalog
- [ ] Barrierefreiheit geprüft
- [ ] Audit- und Betriebs-Log berücksichtigt
- [ ] Neue Abhängigkeiten begründet und geprüft
