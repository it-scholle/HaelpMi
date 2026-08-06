# HaelpMi.Installer.Tests

Coded End-to-End-Tests für den Installer-Lebenszyklus (siehe `TEST-STRATEGY.md` im
Projekt-Root, Abschnitt A). Anders als `HaelpMi.Core.Tests` **verändert dieses Projekt den
Rechner, auf dem es läuft, wirklich**: es installiert/deinstalliert HälpMi mehrfach,
legt/löscht Windows-Dienste, Firewall-Regeln und einen Autostart-Task an.

## ⚠️ Nur auf einer Wegwerf-VM/einem Snapshot ausführen

**Nicht auf einem Rechner mit einer echten, gewollten HälpMi-Installation starten.** Die
Tests löschen `C:\Program Files\HälpMi`, `C:\ProgramData\HaelpMi` (inkl. echter
Geräte-ID/Raumzuordnung) und alle zugehörigen Dienste/Registrierungen ohne Rückfrage.

Empfohlen: eine VM mit einem Snapshot direkt nach Windows-Ersteinrichtung, vor jedem Lauf
zurücksetzen.

## Voraussetzungen

1. **Administratorrechte für die gesamte Testsitzung** - der Installer selbst braucht sie
   (`PrivilegesRequired=admin`, FR-34). Terminal/`dotnet test` muss aus einer bereits
   erhöhten PowerShell/Eingabeaufforderung heraus laufen, sonst hängt jeder Testschritt an
   einem UAC-Prompt, den kein Test beantwortet.
2. Fertig gebaute Installer in `installer\Output\`:
   ```
   dotnet publish src\HaelpMi.Agent\HaelpMi.Agent.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained true -p:PublishReadyToRun=true -o installer\payload
   dotnet publish src\HaelpMi.Config\HaelpMi.Config.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained true -p:PublishReadyToRun=true -o installer\payload
   dotnet publish src\HaelpMi.UpdateService\HaelpMi.UpdateService.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained true -p:PublishReadyToRun=true -o installer\payload
   installer\inno-compiler\ISCC.exe installer\HaelpMi.iss
   installer\inno-compiler\ISCC.exe installer\HaelpMi-Admin.iss
   ```
   (Test-Installer, kein `/DCustomerGroupId`/`/DInstallerPassword` nötig - siehe
   Standardwerte in `HaelpMiCommon.iss.inc`.) Dieses Projekt baut die Installer bewusst
   NICHT selbst - das ist ein separater, langsamer Schritt und gehört in die Build-Pipeline
   davor, nicht in jeden einzelnen Testlauf.
3. Bildschirm nicht gesperrt/im Hintergrund - `WizardAutomation.cs` steuert Inno Setups
   Wizard-Fenster per simulierter Tastatureingabe (`SendKeys`), das braucht ein echtes,
   fokussierbares Desktop (kein Headless-/RDP-Minimize-Betrieb, keine parallele
   Fernsteuerung während des Laufs).

## Ausführen

```
dotnet test tests\HaelpMi.Installer.Tests\HaelpMi.Installer.Tests.csproj -c Release
```

Die neun Tests (`Step10` … `Step90`) laufen **garantiert sequenziell in dieser Reihenfolge**
(`TestPriorityAttribute`/`PriorityOrderer`, `DisableParallelization` in der
`xunit.runner.json`) - sie bauen bewusst aufeinander auf, ähnlich `ALPHA-TESTPLAN.md`.
Bricht ein Schritt ab, bleibt der Rechner in einem Zwischenzustand stehen (siehe
Kommentar am Ende von `LifecycleTests.cs`) - das ist auf einer Wegwerf-VM in Ordnung.

## Ehrlicher Stand (06.08.2026)

Dieses Projekt kompiliert sauber und alle neun Tests werden von xUnit korrekt entdeckt
(`dotnet test --list-tests`, ungefährlich). **Ich habe es noch NICHT end-to-end gegen
echte Installer-Dateien ausgeführt** - das hätte die echte, geteilte Testinstallation auf
dieser Session-Maschine zerstört (Raum "Mission Control", echte Geräte-ID). Die
Wizard-Automatisierung (`WizardAutomation.cs`) ist gegen die gelesene `[Code]`-Sektion der
`.iss`-Skripte sorgfältig hergeleitet (Seitenreihenfolge, Tastatur-Beschleuniger), aber
**vor dem ersten produktiven Einsatz einmal live auf einer Wegwerf-VM verifizieren** - v. a.
Timing (feste Wartezeiten zwischen Wizard-Seiten) ist der wahrscheinlichste Bruchpunkt,
falls eine Test-VM langsamer/schneller reagiert als angenommen.
