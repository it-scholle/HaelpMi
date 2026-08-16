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

**Bugfix 11.08.2026: dieser Schritt läuft jetzt automatisch als Teil von Install-Creator
(Schritt 2 unten) - vor jedem Admin-Installer-Build mit den exakt gleichen Befehlen wie
unten dokumentiert (siehe `RefreshPayloadAsync` in `HaelpMi.InstallCreator/MainWindow.xaml.cs`).**
Vorher blieb das reine Handarbeit, die niemand automatisch erinnert hat - ein vergessener
Durchlauf hier bedeutete, dass Install-Creator klaglos einen Installer mit veraltetem Code
gebaut hat, ohne jede Warnung. Manuell nur noch nötig, wer Schritt 2 nicht über
Install-Creator, sondern direkt per ISCC fährt (siehe "Direkt per ISCC kompilieren" unten):

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

## Schritt 1b: Update-Paket signieren (optional, für Auto-Update)

Nutzerwunsch 09.08.2026: "vollautomatisch, sobald der Admin sich selbst aktualisiert hat" -
dafür muss der Installer selbst schon ein signiertes P2P-Update-Paket für genau diese
Version mitbringen (siehe `HaelpMi.Core/Updates/UpdateSeedImporter.cs`). Dieser Schritt ist
**optional** - ohne ihn baut der Installer genauso wie bisher, nur ohne das automatische
Auto-Seeding (dann bleibt der Weg unten nötig, um eine Version ins P2P-Netz zu bringen).

**Empfohlen (automatisiert, seit 13.08.2026, "Update-Ei"):** Install-Creator selbst signiert
und bettet das Update-Paket ein - kein manuelles `UpdateSigner`/`Compress-Archive`/Kopieren
mehr nötig. Der private Schlüssel liegt dafür verschlüsselt in Vaultwarden (Notiz
`HälpMi-Update-PrivateKey`), nicht als Klartextdatei auf der Build-Maschine:

1. Install-Creator öffnen - beim Start automatisch die Vaultwarden-Karte ausfüllen
   (Server-URL, Konto-E-Mail, Master-Passwort) und auf "Schlüssel laden" klicken. Existiert
   noch kein Schlüssel, "Neuen Schlüssel erzeugen" nutzen - der öffentliche Schlüssel wird
   dabei angezeigt/in die Zwischenablage kopiert und muss einmalig manuell in
   `HälpMi\src\HaelpMi.Core\Updates\UpdateSignaturePublicKey.cs` eingetragen werden (bewusst
   kein automatisches Editieren von Quelltext durch das Tool).
2. "Admin-Installer erstellen" klicken (kein separates Häkchen mehr nötig seit 16.08.2026 -
   das Update-Paket wird automatisch signiert und eingebettet, sofern ein Schlüssel geladen
   ist) - landet in `installer/payload/update-seed/`, bevor ISCC läuft.
3. "Update-Paket veröffentlichen" (ohne ISCC/Kundengruppen-ID/Installer-Passwort) schreibt
   zusätzlich in den lokalen P2P-Cache dieser Maschine, falls HälpMi hier installiert ist -
   nützlich zum Vorbereiten, macht aber **keine** Maschine selbst auf die neue Version:
   ohne mindestens einen Peer, der die Version bereits tatsächlich AUSFÜHRT, beobachtet nie
   jemand einen neueren Boot-Call und die P2P-Kaskade (Wellen-Rollout, siehe CLAUDE.md)
   startet nie von selbst.
4. **"Update erstellen"** (seit 16.08.2026, löst genau die Lücke aus Punkt 3): baut eine
   einzelne, eigenständig lauffähige Datei (`HaelpMi-Update-<Version>.exe`) - signiertes
   Paket an eine einmal veröffentlichte `HaelpMi.UpdateBootstrapper`-Kopie angehängt. Diese
   eine Datei geht an den Admin; per Doppelklick auf einer bereits installierten Maschine
   aktualisiert sie diese Maschine sofort selbst (Install/Test/Swap gegen den lokal
   laufenden `HaelpMi.UpdateService`, ohne auf einen Peer zu warten) und macht die Version
   danach im "Updates"-Tab des Admin-Dashboards zur Freigabe sichtbar. Das ist der
   vorgesehene Weg, um die allererste Maschine einer Kundengruppe auf eine neue Version zu
   bringen, ohne einen kompletten neuen Installer laufen lassen zu müssen. Ab der
   Ein-Klick-Freigabe im Dashboard verbreitet sich die Version wie gewohnt automatisch
   (in Wellen) an alle anderen erreichbaren Geräte weiter.

**Fallback (manuell, z. B. auf einer Maschine ohne Vaultwarden-Zugriff):**

1. Einmalig (nur beim allerersten Mal): Schlüsselpaar erzeugen und **außerhalb** des
   Repo-Checkouts sichern (die Namenskonvention unten matcht `.gitignore`, falls doch
   versehentlich im Checkout erzeugt):
   ```powershell
   dotnet run --project HälpMi\src\HaelpMi.UpdateSigner -- genkey update-private-key.txt update-public-key.txt
   ```
   Den öffentlichen Schlüssel einmalig in
   `HälpMi\src\HaelpMi.Core\Updates\UpdateSignaturePublicKey.cs` einbetten (siehe
   TECHSTACK-UND-ARCHITEKTUR.md - der dort eingebettete Key ist noch ein Wegwerf-Dev-Key).
   Den privaten Schlüssel nie ins Repo, nie ins Log, nie in eine Fehlermeldung (CLAUDE.md).

2. Payload (aus Schritt 1) zu `package.zip` packen und signieren:
   ```powershell
   Compress-Archive -Path HälpMi\installer\payload\* -DestinationPath package.zip -Force
   dotnet run --project HälpMi\src\HaelpMi.UpdateSigner -- sign package.zip update-private-key.txt <Version, z.B. 0.8.0> manifest.json
   ```

3. Beide Dateien nach `HälpMi\installer\payload\update-seed\` kopieren, **bevor** du Schritt
   2 (Install-Creator) ausführst - das bestehende `[Files]`-Statement
   (`Source: "payload\*"; ... recursesubdirs`) nimmt den Unterordner dann automatisch mit,
   keine .iss-Änderung nötig:
   ```powershell
   New-Item -ItemType Directory -Force HälpMi\installer\payload\update-seed
   Copy-Item package.zip, manifest.json HälpMi\installer\payload\update-seed\
   ```

Jedes damit gebaute Gerät (Admin **und** User - kein Rollen-Sonderfall) importiert sein
mitgebrachtes Paket beim ersten Start automatisch ins lokale P2P-Cache und dient danach als
Quelle für andere Peers. Das ersetzt **nicht** die Freigabe im Admin-Dashboard
(`SharedConfig.UpdateRollout`) - andere Geräte pullen es weiterhin erst, nachdem der Admin
die Version dort einmalig freigegeben hat (CLAUDE.md "Rollout-Freigabe": kein Kontingent mehr,
ab der Freigabe verbreitet sich die Version automatisch von Gerät zu Gerät weiter).

## Schritt 2: Install-Creator - hier baust du den Admin-Installer

**Bugfix 11.08.2026: Verknüpfung/Doppelklick auf `Start.cmd` starten, nicht mehr auf die
exe direkt** - `Start.cmd` (git-getrackt, im Gegensatz zur exe selbst) prüft beim Start, ob
`src\HaelpMi.InstallCreator` neuer ist als die zuletzt veröffentlichte exe daneben, und baut
bei Bedarf automatisch neu, bevor sie startet. Vorher blieb eine veraltete exe unbemerkt
stehen, bis jemand von Hand dran dachte, sie neu zu publishen (siehe Speicher
`haelpmi-tools-installcreator-stale-copy`):

```
HälpMi\tools\InstallCreator\Start.cmd
```

Ein manuelles Neu-Bauen ist dadurch normalerweise nicht mehr nötig - `Start.cmd` erledigt
das selbst. Nur falls du es doch einmal von Hand auslösen willst (z. B. zum Debuggen):

```powershell
dotnet publish HälpMi\src\HaelpMi.InstallCreator\HaelpMi.InstallCreator.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained true -p:PublishReadyToRun=true -o HälpMi\tools\InstallCreator
```

**Wichtig:** Dieses Werkzeug muss innerhalb des Repo-Checkouts liegen bleiben (sucht beim
Start automatisch nach oben nach dem `installer`-Ordner) - nicht einzeln auf einen anderen
Rechner kopieren, ohne den Rest des Repos mitzunehmen.

**Zusätzliche Absicherung seit 16.08.2026:** wer aus diesem Checkout heraus an
`src\HaelpMi.InstallCreator` committet, sollte einmalig `git config core.hooksPath .githooks`
setzen - ein Pre-Commit-Hook baut die Kopie in `tools\InstallCreator` dann automatisch bei jedem
Commit neu, der den Quellcode berührt, statt sich auf `Start.cmd` beim nächsten Start zu
verlassen. Details: `docs/WORKFLOW.md` Abschnitt "Install-Creator-Rebuild-Hook".

In der App:
1. Häkchen "Test-Installer" setzen (für interne Tests) oder Kundenname + Passwort
   eintragen (für eine echte Auslieferung).
2. "Admin-Installer erstellen" klicken - baut jetzt zuerst automatisch den Payload (Schritt 1
   oben) neu, bevor ISCC läuft. Dauert dadurch spürbar länger als früher (mehrere `dotnet
   publish`-Durchläufe), garantiert dafür aktuellen Code statt eines vergessenen Handschritts.
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
