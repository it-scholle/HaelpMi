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
