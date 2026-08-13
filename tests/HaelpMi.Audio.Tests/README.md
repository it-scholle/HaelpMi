# HaelpMi.Audio.Tests

Coded Regressionstest für die echte NAudio/WASAPI-Wiedergabepipeline (`TEST-STRATEGY.md`,
Abschnitt F, F0). Anders als `HaelpMi.Core.Tests` **spielt dieses Projekt wirklich hörbar
den Standard-Alarmton auf jedem aktiven Wiedergabegerät ab** - siehe Klassenkommentar in
`AudioTests.cs` für den Bug, den es abdeckt.

## ⚠️ Läuft NICHT automatisch in jedem `dotnet test`

Bugfix 13.08.2026 (Fehlerbericht "VM piept unregelmäßig, ohne installierte HälpMi-Instanz"):
Der Test lag ursprünglich in `HaelpMi.Core.Tests`, das laut `TEST-STRATEGY.md`/CLAUDE.md
"bei jedem `dotnet test`, keine Systemänderung" automatisch mitläuft. Das Kriterium
"keine Systemänderung" traf technisch zu (nichts bleibt danach anders zurück), hat aber die
hörbare Nebenwirkung übersehen: jeder `dotnet test`-Lauf gegen `HaelpMi.Core.Tests` -
egal von welcher Session/welchem Worktree im selben Repo, auch versehentlich im
Hintergrund - hat den echten Alarmton auf allen Lautsprechern ausgelöst, unmute
erzwungen inklusive. Jetzt in einem eigenen Projekt, genau wie
`HaelpMi.Installer.Tests` nur auf gezielten, expliziten Aufruf hin - siehe dortige
README für das analoge Muster (Ausschluss ist reine Konvention/Dokumentation, keine
harte technische Sperre - `dotnet test` auf Projekt- statt Solution-Ebene aufrufen).

## Ausführen

```
dotnet test tests\HaelpMi.Audio.Tests\HaelpMi.Audio.Tests.csproj
```

Braucht mindestens ein aktives Wiedergabegerät (WASAPI Render/Active), um aussagekräftig
zu sein - ohne eines ist ein leerer Durchlauf (kein Play, kein Hang) ebenfalls ein
gültiges, wenn auch weniger scharfes Ergebnis (siehe Klassenkommentar in `AudioTests.cs`).
Vor dem Lauf: Lautstärke/Umgebung so wählen, dass ein kurzer, lauter Alarmton auf jedem
angeschlossenen Gerät niemanden überrascht.
