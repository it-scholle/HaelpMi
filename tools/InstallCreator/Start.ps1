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
Push-Location $repoRoot
$prevErrorPref = $ErrorActionPreference
$ErrorActionPreference = "Continue"
try {
    git rev-parse --is-inside-work-tree *> $null
    if ($LASTEXITCODE -eq 0) {
        $dirtyStatus = git status --porcelain 2>$null
        if ([string]::IsNullOrWhiteSpace($dirtyStatus)) {
            git fetch --quiet 2>$null
            $pullOutput = (git pull --ff-only 2>&1 | Out-String).Trim()
            if ($LASTEXITCODE -ne 0) {
                Write-Host "Hinweis: automatischer git pull nicht möglich (kein Fast-Forward oder kein Netzwerk) - baue mit dem lokalen Stand weiter."
            } elseif ($pullOutput -notmatch "Already up to date") {
                Write-Host "git pull: $pullOutput"
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
