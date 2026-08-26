# Gemeinsame Rebuild-Logik für tools/InstallCreator/, per "." (source) aus mehreren
# Hooks aufgerufen (pre-commit, post-rewrite, post-merge, post-checkout - siehe
# docs/WORKFLOW.md Abschnitt "Install-Creator-Rebuild-Hook"). KEIN eigenständig
# ausführbares Skript (keine Shebang, kein eigenes chmod +x nötig) - wird immer per "."
# in den jeweiligen Hook eingelesen, damit $0 der Pfad des AUFRUFENDEN Hooks bleibt
# (Bugfix 17.08.2026, siehe ursprünglicher Kommentar in pre-commit: dest_root hängt
# davon ab, dass $0 zuverlässig im Haupt-Checkout liegt).
#
# Bugfix 26.08.2026 (Fehlerbericht "InstallCreator startet noch mit alter Versionsnummer"
# nach einem Merge, der über `git rebase --continue` + `git commit --amend` gelaufen ist):
# ein rein ereignisbasierter Hook (nur pre-commit, nur bei vorhandenem "staged diff") sieht
# weder `git rebase --continue` (führt pre-commit in dieser Git-Version gar nicht aus) noch
# ein `--amend`, das inhaltlich nichts Neues staged (der eigentliche Versionssprung war
# schon im vorherigen, hookless durchgelaufenen Rebase-Schritt committet). Jetzt
# zustandsbasiert statt ereignisbasiert: ein Hash über die beobachteten Pfade wird nach
# jedem erfolgreichen Build in tools/InstallCreator/.source-hash festgehalten und bei
# JEDEM Aufruf dieses Skripts (aus irgendeinem der vier Hooks) mit dem aktuellen Stand
# verglichen - unabhängig davon, WELCHE Git-Operation zu diesem Stand geführt hat. Ein
# Treffer (Hash unverändert) beendet den Aufruf sofort, ohne zu bauen - hält alle vier
# Hooks im Normalfall (nichts an den beobachteten Pfaden geändert) schnell.
#
# REBUILD_ALLOW_ABORT (vom aufrufenden Hook VOR dem Sourcen gesetzt): nur pre-commit
# setzt "1" - dort kann ein erkannter Build-während-sich-die-Quelle-nochmal-ändert-Fall
# den Commit noch hart verhindern. Die anderen drei Hooks laufen NACH einer bereits
# abgeschlossenen Git-Operation (Rebase/Merge/Checkout) - "abbrechen" gibt es dort nicht
# mehr, nur noch loggen.

hook_dir=$(cd "$(dirname "$0")" && pwd)
dest_root=$(dirname "$hook_dir")
source_root=$(git rev-parse --show-toplevel)

csproj="$source_root/src/HaelpMi.InstallCreator/HaelpMi.InstallCreator.csproj"
tools_dir="$dest_root/tools/InstallCreator"
hash_marker="$tools_dir/.source-hash"

if [ ! -f "$csproj" ]; then
    return 0 2>/dev/null || exit 0
fi

hash_watched_paths() {
    (
        cd "$source_root" || exit 1
        find src/HaelpMi.InstallCreator src/HaelpMi.UpdateSigner Directory.Build.props \
            \( -name bin -o -name obj \) -prune -o -type f -print 2>/dev/null \
            | sort | xargs -r sha256sum 2>/dev/null | sha256sum | cut -d' ' -f1
    )
}

hash_before=$(hash_watched_paths)
if [ -z "$hash_before" ]; then
    # Beobachtete Pfade fehlen in diesem Checkout (z. B. sehr alter Stand) - nichts zu tun.
    return 0 2>/dev/null || exit 0
fi

stored_hash=""
if [ -f "$hash_marker" ]; then
    stored_hash=$(cat "$hash_marker" 2>/dev/null || echo "")
fi

if [ "$hash_before" = "$stored_hash" ]; then
    # Unverändert seit dem letzten erfolgreichen Build - der Normalfall bei den allermeisten
    # Hook-Aufrufen, deshalb bewusst der schnelle Pfad ohne dotnet-Aufruf.
    return 0 2>/dev/null || exit 0
fi

echo "Install-Creator-Rebuild-Hook (via $(basename "$0")): Quelle hat sich seit dem letzten Build geändert - baue tools/InstallCreator neu..."

# Idle offen gelassene Vorgänger-Instanz sperrt sonst die exe beim Publish - unkritisch zu
# killen (reines Entwickler-Werkzeug, siehe Speicher install-creator-idle-process-kill-ok).
taskkill //IM HaelpMi.InstallCreator.exe //F >/dev/null 2>&1 || true

# bin/obj vorher löschen, sonst kann ein inkrementeller Build alte Ausgaben unverändert
# wiederverwenden (siehe historischer Bugfix 19.08.2026 im alten pre-commit-Kommentar).
rm -rf "$source_root/src/HaelpMi.InstallCreator/bin" "$source_root/src/HaelpMi.InstallCreator/obj" \
       "$source_root/src/HaelpMi.UpdateSigner/bin" "$source_root/src/HaelpMi.UpdateSigner/obj"

if ! dotnet publish "$csproj" -c Release -r win-x64 -p:Platform=x64 --self-contained true -p:PublishReadyToRun=true -o "$tools_dir"; then
    echo "Install-Creator-Rebuild-Hook: dotnet publish fehlgeschlagen - tools/InstallCreator bleibt vorerst veraltet, bitte manuell nachbauen (Umgebungsproblem, kein Korrektheitsfehler)." >&2
    return 0 2>/dev/null || exit 0
fi

hash_after=$(hash_watched_paths)
if [ "$hash_after" != "$hash_before" ]; then
    # Die Quelle hat sich WÄHREND des Builds nochmal verändert (z. B. eine parallele
    # Session committet gleichzeitig) - das frisch gebaute Artefakt entspricht keinem
    # eindeutigen Quellstand mehr.
    rm -f "$tools_dir/HaelpMi.InstallCreator.exe" "$hash_marker"
    echo "Install-Creator-Rebuild-Hook: FEHLER - Quelle hat sich während des Builds nochmal verändert (Hash-Mismatch). Artefakt verworfen." >&2
    if [ "${REBUILD_ALLOW_ABORT:-0}" = "1" ]; then
        return 1 2>/dev/null || exit 1
    fi
    echo "Install-Creator-Rebuild-Hook: kein Abbruch möglich (Git-Operation bereits abgeschlossen) - nächster Hook-Aufruf holt den Rebuild nach." >&2
    return 0 2>/dev/null || exit 0
fi

echo "$hash_after" > "$hash_marker"
echo "Install-Creator-Rebuild-Hook: tools/InstallCreator neu veröffentlicht (Quelle: $source_root, verifiziert)."
