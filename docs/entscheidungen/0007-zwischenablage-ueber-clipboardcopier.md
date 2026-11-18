# 0007 – Zwischenablage nur über ClipboardCopier

- Datum: 2026-09-01
- Status: angenommen
- Entschieden von: Repo-Owner, nach wiederholten Absturz- und Hänger-Berichten

## Kontext
`System.Windows.Clipboard` wirft auf einem STA-Thread ohne eigene Nachrichtenschleife zuverlässig
einen Fehler, obwohl der Schreibvorgang ankam; `SetDataObject` meldet aus demselben Grund
gelegentlich fälschlich `CLIPBRD_E_CANT_OPEN`.

## Entscheidung
Jeder Zugriff auf die Zwischenablage läuft über eine projekteigene `ClipboardCopier`-Klasse mit
Retry und Rücklese-Prüfung (Vorbild `src/HaelpMi.InstallCreator/ClipboardCopier.cs`). Fehlt sie
in einem Projekt, wird sie dort neu angelegt, weil die Projekte bewusst keine ProjectReference
untereinander haben.

## Folgen
Bewusste Duplikate der Hilfsklasse zwischen Projekten; keine direkten Clipboard-Aufrufe mehr.
