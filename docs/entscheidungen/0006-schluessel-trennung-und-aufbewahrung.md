# 0006 – Getrennte Schlüssel und Aufbewahrung des Update-Schlüssels

- Datum: 2026-08-13 (Vaultwarden), 2026-08-17 (Admin-Rollen-Signatur, nur Legacy)
- Status: angenommen
- Entschieden von: Repo-Owner

## Kontext
Lizenz, Programm-Updates und Installer brauchen unterschiedliche Vertrauensanker. Ein privater
Schlüssel als Klartextdatei auf einer Build-Maschine ist ein vermeidbares Risiko.

## Entscheidung
- Getrennte Schlüssel: Lizenzsignatur, Update-Signatur, optionales Installer-Passwort, später
  Admin-Rollen-Signatur pro Kunden-Gruppe. Nie verwechseln oder zusammenlegen; nur öffentliche
  Schlüssel ins Repo bzw. in die Binary.
- Der private Update-Schlüssel liegt verschlüsselt in Vaultwarden. Der Install-Creator holt ihn per
  Bitwarden-CLI (Master-Passwort einmal pro Programmstart), hält ihn nur im Arbeitsspeicher und
  schreibt ihn nie auf die Platte. Der manuelle Weg über `HaelpMi.UpdateSigner` bleibt Fallback.
- Legacy: Die Admin-Rollen-Signatur machte `Role.Admin` in Boot-Calls nachprüfbar; Bestandsgeräte
  bekamen den Schlüssel per Trust-on-First-Use über einen verschlüsselten P2P-Kanal. Im aktuellen
  Stand nicht vorhanden.

## Folgen
- Die Admin-Rolle ist im aktuellen Stand eine unauthentifizierte Selbstauskunft; eine
  Neuentwicklung der Admin-Rollen-Signatur erfolgt über ein eigenes Ticket.
