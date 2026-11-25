# Legt die zufällige Test-Kundengruppe für Installer-Builds ohne /DCustomerGroupId an, falls sie
# noch fehlt. Aufruf durch HaelpMiKundengruppe.iss.inc beim Kompilieren.
$ErrorActionPreference = 'Stop'
$datei = Join-Path $PSScriptRoot 'test-kundengruppe.txt'
if (-not (Test-Path -LiteralPath $datei)) {
    [IO.File]::WriteAllText($datei, [guid]::NewGuid().ToString('D'))
}
