Baue "HälpMi" – ein LAN-Alarmierungssystem für ein Windows-Verwaltungsnetzwerk. Das vollständige
Pflichtenheft liegt bei (pflichtenheft-lan-alarmierung.md) – lies es zuerst komplett, bevor du
anfängst.

## Wichtig: Zwei Entscheidungen fehlen noch, das ist Absicht

Abschnitt 8a im Pflichtenheft nennt zwei offene Produktentscheidungen (Sicherheitsabfrage vor dem
Senden, ob mehrere Alarmnachrichten unterstützt werden sollen). Diese Antworten liegen noch nicht
vor. Baue trotzdem den kompletten Rest fertig – Abschnitt 9 im Pflichtenheft ist bereits in Phase 1
(jetzt) und Phase 2 (nach den fehlenden Antworten) aufgeteilt. Setze ausschließlich Phase 1 um.

Bereite dabei laut Abschnitt 5.9 aktiv die Erweiterungspunkte für Phase 2 vor, ohne sie schon zu
aktivieren:
- Die Sende-Funktion bekommt von Anfang an einen optionalen "Vor dem Senden bestätigen"-Hook, der
  aktuell einfach übersprungen wird (Standardverhalten: keine Abfrage, sofort senden)
- Nachricht und Tastenkürzel werden intern als Liste modelliert, auch wenn Ersteinrichtung und
  Konfigurationsoberfläche aktuell nur einen einzigen Eintrag anzeigen und zulassen

Wenn dir beim Bauen auffällt, dass eine der beiden offenen Fragen eine Architekturentscheidung
erzwingt, die sich nicht sauber offenhalten lässt: stoppe an der Stelle, dokumentiere kurz warum,
und mach mit dem Rest weiter statt zu raten.

## Kernprinzip

Kein Admin, keine zentrale Instanz. Jeder Arbeitsplatz ist gleichberechtigt, Sender und Empfänger
zugleich, mit einer rein lokal gespeicherten Sicht auf die anderen Geräte im Netz.

Das gesamte Projekt wird unter MIT-Lizenz als Open Source veröffentlicht (feste Vorgabe der Stadt
als Auftraggeberin). Lege von Anfang an eine `LICENSE`-Datei mit MIT-Text und eine `.gitignore`
gegen typische Secret-Dateimuster an. Es gibt eine separate, hier noch nicht spezifizierte
Kunden-Lizenzverwaltung (Ed25519-signierte Lizenzdateien) – dafür wird im Code nur der öffentliche
Schlüssel zur Prüfung eingebaut, niemals ein privater Schlüssel oder sonstiges Geheimnis. Wenn dir
beim Bauen irgendwo ein Schlüssel, Zertifikat oder Zugangsdatum unterkommt, gehört das nicht ins
Repository.

## Was du bauen sollst (Phase 1, siehe Pflichtenheft Abschnitt 9)

1. Grundkommunikation: jedes Gerät ist gleichzeitig TCP-Server und -Client, direkte Verbindungen,
   kein Broker. Persistente Geräte-Kennung (GUID) pro Installation, nicht die IP-Adresse, zum
   Wiedererkennen von Einträgen.
2. Start-Broadcast: jedes Gerät sendet bei jedem eigenen Start einmalig seine Identität per
   UDP-Broadcast; empfangende Geräte aktualisieren ihre lokale Liste und antworten direkt mit der
   eigenen Identität zurück. Kein Heartbeat.
3. Erzwungenes Popup beim Empfänger (im Vordergrund, auch über Vollbildanwendungen) + Signalton auf
   allen aktiven Audio-Ausgabegeräten inkl. temporärer Mute-Übersteuerung. Popup muss aktiv
   quittiert werden.
4. Empfangsbestätigung: Empfänger meldet sich nach Anzeige beim Absender zurück; Absender zeigt ein
   kleines, halbtransparentes Eck-Banner ("Empfangen: x"), das bis zum manuellen Wegklicken stehen
   bleibt.
5. Ein einziges Konfigurationsfenster mit zwei Bereichen:
   - "Allgemein" (eigener Arbeitsplatz): Name/Nutzer/Raum editierbar (löst Re-Broadcast +
     Überschreiben bei allen anderen aus), eigenes Tastenkürzel, eigene Alarmnachricht, eigener
     Signalton für eingehende Alarme (gilt für alle Absender gleich)
   - "Individuell" (bekannte andere Geräte): Tabelle mit Favorit, Checkbox "wird benachrichtigt",
     Notiz editierbar; Name/Raum/Nutzer nur Anzeige, nicht editierbar. Neu entdeckte Geräte werden
     direkt in dieser Liste mit einer "Neu"-Kennzeichnung hervorgehoben, kein separates Popup, keine
     Snooze-Funktion. Die Hervorhebung verschwindet, sobald die Checkbox einmal bewusst gesetzt oder
     aktiv gelassen wurde.
6. Ersteinrichtung beim allerersten Start: Name/Nutzer/Raum als Pflichtfelder, Tastenkürzel
   optional/überspringbar. Erzeugt die Geräte-Kennung.
7. Autostart-Selbstregistrierung im Task-Planer ("bei Anmeldung ausführen"), keine SYSTEM-Session.
   Installer legt zusätzlich eine Startmenü-Verknüpfung fürs Konfigurationsprogramm an.
8. Installer mit Versionserkennung: erkennt bestehende Installation, vergleicht Version, bietet
   Update statt Neuinstallation an, ohne die lokale Geräteliste zu verlieren.
9. Selbsttest-Funktion ("Testalarm an mich selbst senden").
10. Bildschirmschoner (nicht Sperrbildschirm, nicht Ruhezustand) wird bei eingehendem Alarm
    automatisch beendet bzw. gar nicht erst gestartet. Der Sperrbildschirm bleibt bewusst
    unangetastet – das ist eine Windows-Sicherheitsgrenze, kein zu lösendes Problem.
11. Automatisierte Tests für die Kernfunktionen (siehe Pflichtenheft NFR-9).

## Technologie

.NET 8, C#, WPF. NAudio für Mehrgeräte-Audio. `System.Net.Sockets` für die Kommunikation. Win32
`RegisterHotKey` für den globalen Hotkey. Details, bekannte Stolpersteine (Forced-Foreground,
Session-0-Isolation, DHCP/IP-Wechsel) und die volle Anforderungsliste stehen im Pflichtenheft –
bitte dort nachschlagen, nicht neu erfinden.

## Am Ende von Phase 1

Fasse kurz zusammen, was gebaut wurde, was von den vorbereiteten Erweiterungspunkten (Hook,
Listen-Datenmodell) wo im Code sitzt, und was noch fehlt, damit ich das für Phase 2 unverändert
weitergeben kann, sobald die zwei offenen Entscheidungen aus Abschnitt 8a beantwortet sind.
