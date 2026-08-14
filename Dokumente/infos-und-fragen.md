# HälpMi — Infos & Fragen fürs Amt

Kurze Merkliste offener Punkte, die eine externe Entscheidung/Abstimmung brauchen — nicht
im Detail, nur zum Mitnehmen ins Gespräch. Stand: 05.08.2026.

## Datenschutz / Personalrat
- **Änderungshistorie + Antwort-Log zeigen Klarnamen** — mit Personalrat abzustimmen, laut
  Pflichtenheft bereits als offen markiert, nicht eigenmächtig erweitert.
  Klarstellung 15.08.2026: das neue revisionssichere Audit-Log (AuditSyncService, Push an
  Admin-Geräte) berührt diesen Punkt **nicht** — es bleibt bei Geräte-ID + Zeitstempel,
  keine Klarnamen, keine Erweiterung des Empfängerkreises über Admin-Geräte hinaus.
- **RDP-Kennzeichnung** (wer sitzt gerade per Fernzugriff am Gerät) — zusätzliche
  personenbezogene Datenverarbeitung ggü. Version 1, braucht eigenes Personalrat-Signoff.

## Sicherheit
- **Netzwerkverkehr ist aktuell unverschlüsselt** — Alarmtext, Namen, Räume für jeden im
  selben Netzsegment lesbar, Nachrichten theoretisch fälschbar. Nachrüsten machbar, mittlerer
  Aufwand (kein Rewrite). Empfehlung: ja machen, gerade weil personenbezogen/sicherheitsrelevant.
- **Raum/Raumnummer im User-Fenster jetzt editierbar** mit nur einem Hinweis-Popup, keiner
  echten Zugriffskontrolle. Ein echter Windows-Admin-Prompt (UAC) ist als Ausbaustufe
  vorgesehen, noch nicht gebaut.

## Noch nicht entschieden
- **Ton pro Alarm-Profil** statt nur pro Gerät (jedes Profil könnte einen eigenen
  Empfangston bekommen) — noch nicht gewünscht/entschieden.
- **"Individuell - bekannte Geräte"-Liste im User-Fenster** — Mehrwert für den normalen
  User fraglich, seit Admin-Dashboard die Zuordnung übernimmt. Behalten oder entfernen?
- **Framework-dependent statt self-contained Installer** — würde Installer deutlich
  verkleinern/schneller installieren, braucht aber vorinstalliertes .NET 8 Desktop Runtime
  auf jedem Zielrechner. Tauschgeschäft, noch nicht entschieden.

## Bekannt, für später vorgemerkt (nicht dringend)
- **Admin-Rollen-Verifizierung fürs Audit-Log** (15.08.2026): `AuditSyncService` prüft
  einen Peer aktuell nur über dessen unauthentifizierte `Role`-Selbstauskunft, genau wie
  EditLockService/ConfigSyncService (keine Regression, aber neu ist, dass hier vertrauliche
  Log-Daten an ein ungeprüftes "Admin"-Gerät gelangen könnten). Saubere Lösung nur mit
  einem vierten (asymmetrischen) Schlüsselpaar — privater Schlüssel nur in Admin-
  Installern, öffentlicher überall, geprüft von AuditSync/EditLock/ConfigSync gemeinsam.
  Berührt Install-Creator (Schlüssel-Erzeugung) und die schon produktiven EditLockService/
  ConfigSyncService, deshalb bewusst als eigener Folge-Task vorgemerkt, nicht Teil der
  aktuellen Umsetzung.
- **Admin-Dashboard-Anzeige der gesammelten Audit-Logs** (15.08.2026): `AuditIngestStore`
  legt empfangene Log-Kopien pro Ursprungsgerät strukturiert ab, hat aber noch keine
  Anzeige im Dashboard — bewusst als eigener Folge-Task vorgemerkt (Anforderungen zu
  Filterung/Lücken-Darstellung/Berechtigung erst klären, siehe Prompt-Vorlage aus der
  Planungssession vom 15.08.2026).
- ~~System-Tray-Icon mit Dashboard-Zugriff~~ — umgesetzt am 05.08.2026, vorgezogen auf
  direkten Wunsch (ursprünglich erst für nach dem Alpha-Test angekündigt).
- Gossip-Geräteliste ist aktuell auf ein paar hundert Geräte ausgelegt (Datagramm-Grenze) -
  bei deutlich größerer Stadt-weiter Ausrollung ggf. nachschärfen.
- **Install-Creator: Auswahl für bestehende produktive Kundensysteme** (06.08.2026,
  Nutzerhinweis beim Layout-Umbau) — aktuell erzeugt jeder Lauf eine neue Kunden-Gruppen-ID;
  zum Neuerstellen eines Installers für einen SCHON existierenden Produktivkunden (z. B. nach
  einem Programm-Update) fehlt noch eine Möglichkeit, eine bestehende ID wiederzuverwenden
  statt versehentlich eine neue (und damit einen zweiten, vom ersten isolierten Kreis) zu
  erzeugen. Noch nicht spezifiziert, nur vorgemerkt.
