# Bugfix 11.08.2026 (Fehlerbericht "warum wird die nicht automatisch aktualisiert?!"):
# HaelpMi.InstallCreator.exe in diesem Ordner ist eine von Hand veröffentlichte Kopie
# (siehe .gitignore-Kommentar), die selbst nicht weiß, ob ihr eigener Quellcode
# (src/HaelpMi.InstallCreator) sich seither geändert hat - das führte schon einmal zu
# stundenlanger Verwirrung (veraltete Icon-Buttons, siehe Speicher
# haelpmi-tools-installcreator-stale-copy). Dieses Skript ist ab jetzt der einzige Weg,
# wie Install-Creator gestartet werden soll (Desktop-Verknüpfung hierauf umbiegen, nicht
# mehr auf die exe direkt): vergleicht Quellcode- gegen exe-Zeitstempel, baut bei Bedarf
# automatisch neu, startet danach die (dann garantiert aktuelle) exe.
$ErrorActionPreference = "Stop"

$toolsDir = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $toolsDir "..\..")).Path
$exePath = Join-Path $toolsDir "HaelpMi.InstallCreator.exe"
$srcDir = Join-Path $repoRoot "src\HaelpMi.InstallCreator"
$csproj = Join-Path $srcDir "HaelpMi.InstallCreator.csproj"

# Ergänzt 27.08.2026 (Nutzerwunsch: nicht mehr von Hand pullen müssen, bevor der
# Install-Creator gestartet wird) - frischt den Checkout hier automatisch von origin,
# bevor unten anhand der Zeitstempel geprüft wird, ob neu gebaut werden muss. Nur
# Fast-Forward und nur bei sauberem Arbeitsbaum: ein Merge/Force würde sonst lokale,
# noch nicht committete Änderungen überschreiben können - in dem Fall lieber
# überspringen und mit dem lokalen Stand weiterbauen, als unbeaufsichtigt etwas zu
# verlieren. Ist `core.hooksPath .githooks` aktiv, stößt der Pull über `post-merge`
# ohnehin denselben Rebuild-Hook an, der hash-basierte Rebuild unten fängt den Fall
# aber auch ab, falls die Hooks auf dieser Maschine nicht aktiviert sind.
#
# Nachbesserung 27.08.2026 (Nutzerfeedback: Install-Creator startet seit obiger Aenderung
# spuerbar langsamer): gemessen 5-10 Sekunden allein fuer den Handshake von "git fetch"
# gegen GitHub, unabhaengig davon, ob es ueberhaupt etwas Neues gibt - reine Auth-/
# Verbindungslatenz, kein Datenvolumen (.git ist nur wenige MB gross). Bei jedem einzelnen
# Start diesen Preis zu zahlen waere fuer den eigentlichen Zweck (Start des Tages nicht
# veraltet, nicht: jeder einzelne Neustart binnen Minuten taggenau) unverhaeltnismaessig -
# deshalb Drossel per Marker-Datei: ein Fetch/Pull passiert hoechstens alle 10 Minuten,
# dazwischen startet das Tool sofort mit dem zuletzt bekannten Stand.
$pullThrottleMinutes = 10
$lastPullMarker = Join-Path $toolsDir ".last-pull-check"
$dueForPullCheck = $true
if (Test-Path $lastPullMarker) {
    $lastCheck = [datetime]::MinValue
    $lastCheckText = Get-Content -Path $lastPullMarker -Raw -ErrorAction SilentlyContinue
    $parsed = [datetime]::TryParse(
        $lastCheckText, [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Globalization.DateTimeStyles]::RoundtripKind, [ref]$lastCheck)
    if ($parsed -and ((Get-Date).ToUniversalTime() - $lastCheck).TotalMinutes -lt $pullThrottleMinutes) {
        $dueForPullCheck = $false
    }
}

if ($dueForPullCheck) {
    Push-Location $repoRoot
    $prevErrorPref = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        git rev-parse --is-inside-work-tree *> $null
        if ($LASTEXITCODE -eq 0) {
            $dirtyStatus = git status --porcelain 2>$null
            if ([string]::IsNullOrWhiteSpace($dirtyStatus)) {
                git fetch --quiet 2>$null
                $currentBranch = (git symbolic-ref -q --short HEAD 2>$null)
                if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($currentBranch)) {
                    $pullOutput = (git pull --ff-only 2>&1 | Out-String).Trim()
                    if ($LASTEXITCODE -ne 0) {
                        Write-Host "Hinweis: automatischer git pull nicht möglich (kein Fast-Forward oder kein Netzwerk) - baue mit dem lokalen Stand weiter."
                    } elseif ($pullOutput -notmatch "Already up to date") {
                        Write-Host "git pull: $pullOutput"
                    }
                } else {
                    # Bugfix 10.09.2026 (Fehlerbericht "Installer zeigt noch alte Version,
                    # obwohl release-1.0-MVP laengst aktueller ist", Issue #109): `git pull`
                    # oben setzt einen Branch mit Upstream voraus - bei einem detached HEAD
                    # (kein Branch) schlägt es IMMER fehl, unabhängig von Netzwerk/Fast-
                    # Forward, und landete bisher in derselben Zeile wie ein harmloser
                    # Offline-Fall ("kein Fast-Forward oder kein Netzwerk") - die eigentliche
                    # Ursache (detached HEAD) wurde nie benannt, der Build lief unbemerkt mit
                    # dem alten Stand weiter. `git remote set-head origin -a` frischt den
                    # origin/HEAD-Symref (kann nach einem Default-Branch-Wechsel auf GitHub
                    # veraltet sein, siehe CLAUDE.md-Abschnitt zum rotierenden
                    # Default-Branch), danach wird - wie beim Branch-Pull oben nur per
                    # Fast-Forward - direkt auf origin/HEAD aktualisiert.
                    git remote set-head origin -a *> $null
                    git rev-parse --verify --quiet origin/HEAD *> $null
                    if ($LASTEXITCODE -eq 0) {
                        $currentCommit = (git rev-parse HEAD).Trim()
                        $targetCommit = (git rev-parse origin/HEAD).Trim()
                        if ($currentCommit -ne $targetCommit) {
                            git merge-base --is-ancestor HEAD origin/HEAD
                            if ($LASTEXITCODE -eq 0) {
                                git checkout --detach --quiet origin/HEAD *> $null
                                if ($LASTEXITCODE -eq 0) {
                                    $shortTarget = (git rev-parse --short origin/HEAD).Trim()
                                    Write-Host "Detached HEAD war hinter origin/HEAD zurück - automatisch auf $shortTarget vorgezogen."
                                } else {
                                    Write-Host "WARNUNG: detached HEAD haette auf origin/HEAD vorgezogen werden koennen, git checkout ist aber fehlgeschlagen - baue mit dem lokalen (veralteten) Stand weiter."
                                }
                            } else {
                                Write-Host ""
                                Write-Host "=================================================================="
                                Write-Host "WARNUNG: dieser Checkout steht auf einem detached HEAD, der NICHT"
                                Write-Host "Vorfahre von origin/HEAD ist (divergiert) - kann nicht automatisch"
                                Write-Host "aktualisiert werden. Ein jetzt gebauter Installer spiegelt"
                                Write-Host "moeglicherweise NICHT den aktuellen origin/HEAD-Stand wider."
                                Write-Host "Manuell pruefen: git log --oneline -5   bzw.   git checkout origin/HEAD"
                                Write-Host "=================================================================="
                                Read-Host "Enter zum Fortfahren trotz moeglicherweise veraltetem Stand"
                            }
                        }
                    } else {
                        Write-Host "Hinweis: detached HEAD, origin/HEAD nicht aufloesbar (kein Netzwerk?) - baue mit dem lokalen Stand weiter."
                    }
                }
            } else {
                # Grossgeschriebenes "Ä" ohne BOM wird von PowerShell 5.1 ohne UTF-8-BOM als
                # typografisches Anführungszeichen fehlinterpretiert und würde den String hier
                # vorzeitig beenden - deshalb "Aenderungen" statt "Änderungen" in dieser Zeile.
                Write-Host "Hinweis: lokale, nicht committete Aenderungen im Checkout - automatischer git pull uebersprungen, baue mit dem lokalen Stand weiter."
            }
        }
    } finally {
        $ErrorActionPreference = $prevErrorPref
        Pop-Location
        # Zeitstempel wird auch bei Fehlschlag (kein Netzwerk o. Ä.) geschrieben - sonst
        # wuerde ein Offline-Start bei jedem weiteren Start erneut die volle Handshake-
        # Wartezeit erzwingen, bis wieder Netzwerk da ist.
        (Get-Date).ToUniversalTime().ToString("o") | Set-Content -Path $lastPullMarker -NoNewline
    }
}

if (-not (Test-Path $csproj)) {
    Write-Host "Fehler: $csproj nicht gefunden - läuft dieses Skript innerhalb des Repo-Checkouts?"
    Read-Host "Enter zum Schließen"
    exit 1
}

# Haertung 19.08.2026 (Nutzerwunsch, nachdem sich herausstellte, dass die Startmenue-
# Verknuepfung monatelang an der rohen exe statt hier vorbeigelaufen war): dieselben drei
# Pfade beobachten wie der Pre-Commit-Hook (.githooks/pre-commit), nicht nur
# src/HaelpMi.InstallCreator - sonst bliebe z.B. ein reiner Directory.Build.props-Versions-
# Bump (aendert die im Fenstertitel angezeigte Version) oder eine HaelpMi.UpdateSigner-
# Aenderung (wird mit hineinkompiliert) von diesem Skript unbemerkt, waehrend der Hook es
# laengst als "Rebuild noetig" gewertet haette - zwei Sicherheitsnetze mit unterschiedlicher
# Maschenweite sind schlechter als eines mit der breiteren.
$updateSignerDir = Join-Path $repoRoot "src\HaelpMi.UpdateSigner"
$buildProps = Join-Path $repoRoot "Directory.Build.props"
$watchedSourceDirs = @($srcDir, $updateSignerDir) | Where-Object { Test-Path $_ }

$needsRebuild = $true
if (Test-Path $exePath) {
    $exeTime = (Get-Item $exePath).LastWriteTimeUtc
    # bin/obj sind eigene .gitignore-Einträge (Build-Zwischenstände), zählen hier nicht als
    # "echter" Quellcode-Zeitstempel - würden sonst nach jedem Zwischen-Build eine Neu-
    # Veröffentlichung erzwingen, obwohl sich am eigentlichen Code nichts geändert hat.
    $newestSource = $watchedSourceDirs |
        ForEach-Object { Get-ChildItem -Path $_ -Recurse -File } |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    $newestBuildProps = if (Test-Path $buildProps) { Get-Item $buildProps } else { $null }

    $newestRelevantTime = @($newestSource, $newestBuildProps) |
        Where-Object { $null -ne $_ } |
        ForEach-Object { $_.LastWriteTimeUtc } |
        Sort-Object -Descending |
        Select-Object -First 1

    if ($null -ne $newestRelevantTime -and $newestRelevantTime -le $exeTime) {
        $needsRebuild = $false
    }
}

if ($needsRebuild) {
    Write-Host "Quellcode neuer als die veröffentlichte exe (oder noch keine da) - baue HaelpMi.InstallCreator neu..."

    # Idle offen gelassene Vorgänger-Instanz sperrt sonst die exe-Datei beim Publish -
    # unkritisch zu killen (reines Entwickler-Werkzeug, kein laufender Build/Passwort-
    # Eingabe-Zustand geht dabei verloren, siehe Speicher install-creator-idle-process-kill-ok).
    Get-Process -Name "HaelpMi.InstallCreator" -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 300

    & dotnet publish $csproj -c Release -r win-x64 -p:Platform=x64 --self-contained true -p:PublishReadyToRun=true -o $toolsDir
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Neu-Bauen fehlgeschlagen (dotnet-publish-Exitcode $LASTEXITCODE) - breche ab."
        Read-Host "Enter zum Schließen"
        exit 1
    }
}

Start-Process -FilePath $exePath
