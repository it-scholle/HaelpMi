# HälpMi – Bauen und Installieren

Diese eine Datei ersetzt die früheren, sich überschneidenden Anleitungen. Alle Pfade hier
sind vollständig ab dem Projekt-Root angegeben (dem Ordner, der `HälpMi.sln` enthält),
damit sie sich unabhängig vom eigenen Checkout-Pfad wiederfinden lassen.

Diese Sitzung hat den kompletten Ablauf einmal real durchgespielt (echtes Inno Setup 7,
nicht nur Code gelesen) - was unten steht, ist tatsächlich geprüft, nicht nur behauptet.

## Überblick: was am Ende existiert

- **Ein** Installer-Typ wird von dir gebaut: der **Admin-Installer**. Er bringt alles mit,
  was für einen normalen Arbeitsplatz-Rechner nötig ist, PLUS das Admin-Dashboard, PLUS
  einen Bausatz, aus dem das Dashboard später selbst einen **User-Installer** exportiert.
- Du gibst dem Sysadmin beim Kunden **nur diese eine Admin-Installer-Datei**. Alles
  Weitere (User-Installer für die übrigen Arbeitsplätze) exportiert er sich selbst aus dem
  laufenden Admin-Dashboard heraus - siehe Abschnitt "User-Installer exportieren" unten.

## Einmalige Einrichtung (nur auf deinem eigenen Rechner, nicht beim Kunden)

1. [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
2. Inno Setup (aktuell Version 7, 6 geht laut Hersteller auch) von
   <https://jrsoftware.org/isdl.php> - kostenlos, normal installieren.
3. Kompletten Inhalt deiner Inno-Setup-Installation (nicht nur `ISCC.exe` - welche
   Begleitdateien der Compiler exakt braucht, lässt sich nicht zuverlässig eingrenzen,
   daher lieber alles) hierher kopieren:
   ```
   HälpMi\installer\inno-compiler\
   ```
   Typische Quelle: `C:\Program Files\Inno Setup 7\` oder
   `%LocalAppData%\Programs\Inno Setup 7\`.
4. Dieser Ordner ist in `HälpMi\.gitignore` eingetragen (Drittanbieter-Binärdateien,
   nicht Teil des Repos) - muss aber lokal existieren, bevor du einen Admin-Installer
   bauen kannst.

Das war's - kein weiterer manueller Schritt. Alles Folgende automatisiert Install-Creator.

## Schritt 1: Payload bauen

Im Projekt-Root (`HälpMi\`):

```powershell
dotnet publish HälpMi\src\HaelpMi.Agent\HaelpMi.Agent.csproj         -c Release -r win-x64 -p:Platform=x64 --self-contained true -p:PublishReadyToRun=true -o HälpMi\installer\payload
dotnet publish HälpMi\src\HaelpMi.Config\HaelpMi.Config.csproj       -c Release -r win-x64 -p:Platform=x64 --self-contained true -p:PublishReadyToRun=true -o HälpMi\installer\payload
dotnet publish HälpMi\src\HaelpMi.UpdateService\HaelpMi.UpdateService.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained true -p:PublishReadyToRun=true -o HälpMi\installer\payload
```

Erzeugt einen ganzen Ordner voller Dateien (`HaelpMi.Agent.exe` + `HaelpMi.Core.dll` +
`HaelpMi.UI.dll` + Laufzeit-DLLs usw., kein einzelnes gebündeltes Exe mehr) unter
`HälpMi\installer\payload\` - das `[Files]`-Statement in `HaelpMiCommon.iss.inc` kopiert
per `recursesubdirs` ohnehin den ganzen Ordner, das war schon immer so vorbereitet.

> **06.08.2026 - `PublishReadyToRun` erneut versucht, diesmal mit Erfolg:** der erste
> Versuch (05.08.2026) hatte tatsächlich zwei voneinander unabhängige Probleme
> übereinandergestapelt: (1) `-r win-x64` ohne explizites `-p:Platform=x64` bei Projekten
> mit `<Platforms>x64;ARM64</Platforms>` - jetzt immer explizit mitgegeben, PE-Header aller
> vier Exen + ihrer eigenen (R2R-kompilierten) DLLs jedes Mal direkt nachgeprüft (Machine
> = 0x8664), nicht nur "hat kompiliert" vertraut. (2) `-p:PublishSingleFile=true` UND
> `-p:PublishReadyToRun=true` gemeinsam sind selbst ein zusätzlicher Risikofaktor
> (Bundle-Extraktion + R2R-Bild ist eine seltener getestete Kombination) - deshalb jetzt
> bewusst OHNE Single-File: ein normaler, mehrdateiiger self-contained-Ordner startet
> nachweislich spürbar schneller (die eigenen App-DLLs sind jetzt vorkompiliert statt bei
> jedem Start kalt zu jitten, UND es entfällt die Bundle-Öffnen-Overhead beim Start), ohne
> dass das am Installer irgendetwas ändert (der kopiert ohnehin den ganzen Payload-Ordner).
> Vor jedem Ausliefern trotzdem: PE-Header prüfen UND die Datei einmal wirklich starten
> lassen, nicht nur den Compiler-Exitcode ansehen - das bleibt Pflicht, kein Vertrauen auf
> "hat compiliert" allein.

> Läuft dein Rechner nicht auf x64 (z. B. ARM64): `-r win-x64 -p:Platform=x64` funktioniert
> trotzdem, .NET kann problemlos für eine andere Ziel-Architektur "cross-publishen" - genau
> das war beim ersten R2R-Versuch aber der ungeprüfte, riskante Teil.

Diesen Schritt nur nach Codeänderungen wiederholen müssen - für reines Neu-Bauen des
Installers mit anderer Kunden-Gruppen-ID reicht Schritt 2.

## Schritt 2: Install-Creator - hier baust du den Admin-Installer

Fertig gebautes, doppelklickbares Werkzeug (schon für dich erstellt):

```
HälpMi\tools\InstallCreator\HaelpMi.InstallCreator.exe
```

Falls du es nach einer Codeänderung neu bauen willst:

```powershell
dotnet publish HälpMi\src\HaelpMi.InstallCreator\HaelpMi.InstallCreator.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained true -p:PublishReadyToRun=true -o HälpMi\tools\InstallCreator
```

**Wichtig:** Dieses Werkzeug muss innerhalb des Repo-Checkouts liegen bleiben (sucht beim
Start automatisch nach oben nach dem `installer`-Ordner) - nicht einzeln auf einen anderen
Rechner kopieren, ohne den Rest des Repos mitzunehmen.

In der App:
1. Häkchen "Test-Installer" setzen (für interne Tests) oder Kundenname + Passwort
   eintragen (für eine echte Auslieferung).
2. "Admin-Installer erstellen" klicken.
3. Ergebnis liegt unter `HälpMi\installer\Output\HaelpMi-Setup-Admin-<Version>.exe` -
   **das** ist die eine Datei für den Sysadmin.

Das Protokollfenster zeigt die verwendete Kunden-Gruppen-ID (nie das Passwort - das landet
absichtlich nirgends, auch nicht im Log).

## Schritt 3: Admin-Installer installieren

- Datei ausführen, UAC-Bestätigung (braucht Adminrechte für die maschinenweite
  Installation, FR-34 - der laufende Agent selbst bleibt trotzdem rechtelos).
- Seite "Raum-Zuordnung": Raumbezeichnung + Raumnummer sind Pflichtfelder.
- Nach Abschluss läuft `HaelpMi.Agent.exe` automatisch, Start-Menü-Verknüpfung
  "HälpMi Konfiguration" ist da, Windows-Dienst `HaelpMiUpdateService` läuft.

## Schritt 4: User-Installer exportieren (aus dem laufenden Admin-Dashboard)

1. "HälpMi Konfiguration" öffnen → "Admin-Dashboard öffnen".
2. Oben: "User-Installer exportieren" klicken.
3. Kurze Wartezeit (kompiliert im Hintergrund, nutzt den mitgelieferten Bausatz - kein
   Inno Setup auf diesem Rechner nötig), dann landet die fertige Datei automatisch im
   echten Windows-Downloads-Ordner, mit kurzer Erfolgs-Animation unten rechts.
4. Diese Datei an alle übrigen Arbeitsplätze verteilen - trägt automatisch dieselbe
   Kunden-Gruppen-ID wie der Admin-Installer, aus dem sie exportiert wurde.
5. Beliebig oft wiederholbar (z. B. falls die Datei verlorengeht).

## Direkt per ISCC kompilieren (ohne Install-Creator, z. B. zum schnellen Testen)

```powershell
& "HälpMi\installer\inno-compiler\ISCC.exe" HälpMi\installer\HaelpMi-Admin.iss
& "HälpMi\installer\inno-compiler\ISCC.exe" HälpMi\installer\HaelpMi.iss
```

Ohne `/DCustomerGroupId=...`/`/DIsTestInstaller=...` fällt das auf eine Nullen-ID und
`IsTestInstaller=true` zurück - taugt nur zum lokalen Testen, nie für eine echte
Kunden-Auslieferung (siehe Kommentare in `HälpMi\installer\HaelpMiCommon.iss.inc`).

## Deinstallation

Über "Apps & Features" - entfernt bewusst *alles*, nicht nur den Programmordner: den
laufenden Agent-Prozess, den Task-Planer-Autostart-Eintrag, den Windows-Dienst
`HaelpMiUpdateService`, und nach Rückfrage auch `C:\ProgramData\HaelpMi` (Geräteliste,
Einstellungen, Änderungshistorie). Bei einer stillen/automatisierten Deinstallation wird
automatisch alles entfernt.

## Bekannte Fallstricke

- Beide installierten Geräte müssen im **gleichen Subnetz** sein - Broadcast-Discovery
  funktioniert nicht über VPN/getrennte VLANs.
- Ohne `HälpMi\installer\inno-compiler\ISCC.exe` bricht Install-Creator mit einer klaren
  Fehlermeldung ab (nicht mit einem kryptischen ISCC-Fehler) - siehe Schritt "Einmalige
  Einrichtung" oben.
- Der Agent läuft ohne eigenes Fenster im Hintergrund (kein Tray-Icon) - erreichbar über
  die Konfiguration, nicht über ein sichtbares Programmfenster.

## Testablauf

Die eigentliche Test-Checkliste (was genau durchzuklicken ist und was zurückgemeldet
werden sollte) steht separat in `HälpMi\ALPHA-TESTPLAN.md`.
