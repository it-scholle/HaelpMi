# 0002 – App-interne Admin-Rolle und Kunden-/Gruppen-ID

- Datum: 2026-08-04 (Version 2 der Architekturregeln; Begriffsklärung Kreis/Gruppe am selben Tag)
- Status: angenommen
- Entschieden von: Repo-Owner nach Rücksprache mit dem Kunden

## Kontext
Version 1 der Regeln sah eine reine Peer-Architektur vor („kein Admin, keine zentrale Instanz“,
„kein Admin-Konto, keine zentrale Berechtigungsinstanz“). In der Praxis brauchten Kunden eine
Stelle, die Gruppen, Alarm-Profile und Updates verwaltet, und mehrere Installationen im selben
physischen Netz mussten sich sicher voneinander trennen lassen.

## Entscheidung
- Es gibt eine App-interne Rolle Admin bzw. User, verankert in der Lizenz-/Konfigurationsdatei und
  unabhängig von Windows-Rechten. Der Satz „kein Admin“ aus Version 1 ist bewusst aufgehoben (keine
  Fehlerkorrektur, sondern Weiterentwicklung).
- Jedes Admin+User-Installer-Paar trägt eine feste, im Install-Creator erzeugte Kunden-/Gruppen-ID;
  alle Pakete werden danach gefiltert.
- „Kreis“ bezeichnet nur diese ID-basierte Netz-Isolation; „Gruppe“ ist die einzige
  organisatorische Einheit im Dashboard. Ein früherer Entwurf mit „Kreis“ als eigener
  Dashboard-Zwischenebene wurde korrigiert.
- Weiterhin gilt: kein zentraler Server-Prozess, Kommunikation P2P.

## Folgen
- Alter Code, Kommentare oder Tests, die noch von reiner Peer-Gleichberechtigung ausgehen, werden
  zur Überarbeitung markiert statt ignoriert.
- Exklusiv-Edit-Lock zwischen Admins und Hot-Reload-Config-Sync werden nötig (Regeln in
  `AGENTS.md` Teil B).
- Die Admin-Rolle ist im aktuellen Stand eine unauthentifizierte Selbstauskunft; eine
  kryptografische Prüfung existierte nur im Legacy-Stand (siehe 0006).
