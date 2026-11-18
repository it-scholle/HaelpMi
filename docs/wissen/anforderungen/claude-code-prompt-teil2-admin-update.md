# Claude-Code-Prompt Teil 2 — Admin-Rollen, Konfigurationsverwaltung, Auto-Update

Lies zuerst `CLAUDE.md` (Version 2) — dort stehen die dauerhaften Architekturregeln, die dieser
Prompt voraussetzt, insbesondere die Aufhebung der reinen Peer-ohne-Admin-Architektur aus Teil 1.

Alle Punkte unten sind mit dem Auftraggeber bestätigt und benötigen keine weitere Rückfrage.
Wo eine konkrete technische Entscheidung meine (Claudes) Empfehlung war und nicht explizit vom
Auftraggeber bestätigt wurde, ist das einzeln vermerkt — bei Unsicherheit dort nachfragen,
ansonsten wie beschrieben umsetzen.

## 1. Rollen & Installation
- Zwei Installer-Typen: Admin-Installer und User-Installer, über eine gemeinsame Kunden-/
  Gruppen-ID verknüpft (siehe Abschnitt 6).
- Installation jeweils für alle Windows-Benutzerkonten des Geräts (Machine-Scope, nicht
  User-Scope), inkl. entsprechender Uninstaller-Gründlichkeit für alle Konten.
- Admin-Installer bringt sämtliche Funktionen der User-App mit, jede Funktion einzeln
  aktivierbar/deaktivierbar — der Admin-Rechner kann so gleichzeitig als Testgerät dienen.
- Die App-Rolle (Admin/User) ist ein eigenes Attribut in der Lizenz-/Konfigdatei, unabhängig
  von Windows-Benutzerrechten.

## 2. Ersteinrichtung (im Admin-Installer abgefragt, Pflicht vor Abschluss der Installation)
- Gerätename: automatisch aus dem Windows-Computernamen, nicht editierbar.
- Raumname: Pflichtfeld.
- Raumnummer: Pflichtfeld. Bei Eingabe im Admin-Dashboard sollen bereits vergebene Raumnamen zu
  bestehenden Raumnummern als Vorschlag erscheinen (Autocomplete), um Räume mit mehreren PCs
  konsistent zu benennen.
- Benutzerkonto: dynamisch, immer der aktuell angemeldete Windows-User, keine manuelle Eingabe.
- User-Installer fragt **nichts** davon ab — die reguläre Konfiguration entfällt für User-Konten
  vollständig, das übernimmt ausschließlich der Admin über das Dashboard.

## 3. Anzeige beim Empfänger
- Raum + Raumnummer immer prominent sichtbar.
- Benutzername klein, untergeordnet.
- RDP-Kennzeichnung: `GetSystemMetrics(SM_REMOTESESSION)` auslesen und zusätzlich zum Usernamen
  anzeigen (z. B. kleines Icon oder Zusatz "(Remote)"), um Homeoffice-Sessions von
  Vor-Ort-Sessions zu unterscheiden. Reine lokale API-Abfrage, keine externe Abhängigkeit.

## 4. Admin-Dashboard — Grundfunktionen
- Standard-Hotkey-Kombination pro Kreis festlegbar.
- Raumbezeichnung pro Raum editierbar, mit Autocomplete-Vorschlag (siehe Abschnitt 2).
- Ton konfigurierbar.
- Mehrere Alarm-Profile parallel möglich (z. B. "Notfall Nachbar", "Notfall alle"), jedes Profil
  mit eigenem Hotkey und eigenem Sender-/Empfängerkreis.
- Empfängerkreis pro Sender ist **asymmetrisch** konfigurierbar: pro Hotkey/Profil und pro Sender
  (Einzelgerät oder Gruppe) ein individueller Empfängerkreis, auch gruppenübergreifend definierbar.
  UI: pro Profil eine Liste aller Sender/Gruppen, jede Zeile mit einer Zelle, die den zugehörigen
  Empfängerkreis anzeigt. Klick auf die Zelle (oder Neuanlage eines Empfängerkreises) öffnet eine
  Auswahlansicht aller Nutzer/Gruppen mit Drag-and-Drop-Zuordnung (nutzerfreundlicher als reine
  Checkboxen, daher Drag-and-Drop als primäre Interaktion, Checkbox als Fallback für
  Barrierefreiheit).
- "Alle"-Button: wendet eine Einstellung gleichzeitig auf mehrere Sender/Empfänger an. Zeigt vor
  dem Anwenden eine Warnung, dass bestehende individuelle Konfigurationen überschrieben werden.
- Gruppenverwaltung: Admin kann mehrere Kreise zu Gruppen zusammenfassen, um die Administration
  bei vielen Kreisen zu vereinfachen (z. B. "Haus 1 Etage 1" als Gruppe mit eigenem
  Empfängerkreis).
- Schwellwert X (ab wie vielen Antworten das Empfänger-Alarmfenster ignorierbar wird) ist pro
  Admin und pro einzelnem Alarm-Ereignis/Trigger konfigurierbar, nicht global fix.

## 5. Exklusiv-Edit-Lock (siehe CLAUDE.md, hier die konkrete Umsetzung)
- Beim Öffnen des Admin-Dashboards: TCP-Call "will editieren" an alle im Kreis erreichbaren
  admin-fähigen Geräte.
- Antwortet ein Gerät bereits "ich administriere gerade" → Zugriff verweigert, Meldung im Stil
  des regulären Alarm-Popups ("XY administriert bereits").
- Antwortet niemand → Zugriff gewährt, eigene Sperre wird gesetzt und an Nachfragen weitergegeben.
- Kollisionsfall (gleichzeitiger Call, praktisch Millisekunden-Gleichstand): beide Seiten lehnen
  sich gegenseitig ab, geben sofort wieder frei, Retry nach zufälligem Backoff zwischen 200 und
  800 ms.
- Sperre pro Kreis, nicht global — unterschiedliche Kreise gleichzeitig administrierbar.
- Auto-Freigabe nach 10 Minuten ohne Edit-Aktivität.

## 6. Kunden-/Gruppen-Isolation
- Beim Bauen im Install-Creator wird aus dem eingegebenen Kundennamen eine feste Kunden-/
  Gruppen-ID abgeleitet und in Admin- **und** den zugehörigen User-Installer eingebettet.
- Jedes Netzwerkpaket (Boot-Call, Alarm, Config-Sync, Update-Ankündigung) trägt diese ID.
  Empfangende Instanzen verwerfen Pakete mit abweichender ID vollständig und ohne Reaktion.
- Damit können zwei unabhängige Installationsgruppen im selben physischen Netz koexistieren,
  ohne sich gegenseitig zu sehen oder zu beeinflussen.

## 7. Alarm — Sender-Seite
- Hotkey löst wiederholtes Senden im 5-Sekunden-Takt aus.
- Automatischer Stopp nach 5 Minuten.
- Automatischer Stopp, sobald mindestens 2 Empfänger mit "ich komme"/"bin unterwegs" (ein und
  dasselbe Ereignis, ein Button) reagiert haben.
- Manueller Abbrechen-Button.
- Info-Hover-Popup (kleines Fenster in der Ecke, wie in Teil 1 etabliert): Status "gesendet",
  "Empfangen: X", Liste "Auf dem Weg" mit Usernamen formatiert untereinander, Abbrechen-Button.

## 8. Alarm — Empfänger-Seite
- Alarmfenster mit Priorität 0, nicht deaktivierbar über normale Fenstersteuerung.
- Schließbar erst, wenn mindestens X Empfänger geantwortet haben (X siehe Abschnitt 4).
- Wird es dennoch vorzeitig geschlossen, öffnet es sich beim nächsten empfangenen Alarmsignal
  wieder automatisch.
- Button "bin unterwegs" sendet Rückmeldung an den Sender (Raum + Bezeichnung + Status).
- Auto-Schließen 1 Minute nach dem jeweils letzten empfangenen Alarmsignal — nicht nach fixer
  Zeit ab erstem Signal. Greift also erst, nachdem der Sender aufgehört hat zu senden (manueller
  Abbruch, 2 Bestätigungen erreicht oder 5-Minuten-Timeout).

## 9. Boot-Verhalten
- Bei Rechnerstart: Broadcast "ich bin da" mit Kunden-/Gruppen-ID, Geräte-GUID, aktueller
  Programmversion, aktueller Config-Version.
- Jeder Boot-Call ist Call-and-Response: beide beteiligten Geräte tauschen ihren vollständigen
  Info-Stack aus, unabhängig davon, wer Absender des ursprünglichen Calls war. Das Gerät mit der
  jeweils neueren Version signalisiert dem anderen ein verfügbares Update.

## 10. Konfigurations-Sync (Hot-Reload)
- Speichern + Verbreiten automatisch bei Blur eines gültig ausgefüllten Pflichtfelds, kein
  Save-Button.
- Verbreitung per Broadcast inkl. neuer Config-Versionsnummer, analog zum Boot-Call.
- Empfangende Geräte laden die Konfiguration im laufenden Prozess neu (Hot-Reload) — **keine**
  Parallelinstanz, kein Swap, das ist ausschließlich der Programm-Update-Pipeline (Abschnitt 11)
  vorbehalten.
- Änderungshistorie pro Kreis (letzte n Einträge) mit Undo-Möglichkeit.

## 11. Programm-Update-Pipeline (0-Downtime)
- Update-Pakete sind mit einem separaten Update-Signaturschlüssel signiert (siehe CLAUDE.md,
  nicht identisch mit dem Lizenzschlüssel). Ungültig signierte Pakete werden verworfen.
- Ablauf pro Gerät:
  1. Neue Version wird als **parallele Instanz** installiert, alte Instanz läuft unverändert weiter
     und behält den Produktiv-Port.
  2. Neue Instanz führt einen lokalen Testping auf einem separaten Test-Port durch.
  3. Zusätzlich wird eine Bestätigung von mindestens einem Peer über erfolgreiche
     Testkommunikation eingeholt, bevor produktiv umgeschaltet wird.
  4. Bei Erfolg: neue Instanz übernimmt den Produktiv-Port, alte Instanz wird gestoppt und
     deinstalliert.
  5. Neue Instanz führt danach einen regulären Boot-Call aus ("ich bin da" mit ID, Version,
     Config-Version) — dadurch verbreitet sich das Update von Gerät zu Gerät weiter.
  6. Bei Fehler in einem der Schritte: Update deinstalliert sich selbst automatisch, sperrt sich
     24 Stunden lang für erneute Versuche auf diesem Gerät, sendet Fehlermeldung inkl. Log und
     Systeminfo an den Admin.
- Rechte/Prozessarchitektur (**Claude-Empfehlung, vom Auftraggeber implizit angenommen, bei
  Unsicherheit dort nachfragen**): ein separater Windows-Dienst (LocalService/SYSTEM) übernimmt
  ausschließlich Installieren/Testen/Swappen/Deinstallieren. Kommunikation Dienst ↔ User-App
  ausschließlich lokal über Named Pipes, kein Netzwerk-Traffic zwischen Dienst und App. Die
  User-App selbst bleibt weiterhin ohne erhöhte Rechte.
- Gestaffelter Rollout pro Kreis, unabhängig von anderen Kreisen entscheidbar:
  - Admin gibt Freigabestufen frei (z. B. 1 → 2 → 4 → 8 Geräte).
  - Ein Gerät gibt ein Update beim Boot-Call nur weiter, wenn es selbst bereits freigegeben ist
    UND das aktuelle Freigabekontingent der Stufe noch nicht ausgeschöpft ist.
  - Erst nach Erfolgsmeldung einer Stufe erhöht der Admin das Kontingent für die nächste Stufe.
  - Not-Aus: bei mehreren Fehlschlägen innerhalb einer Stufe (Schwellwert konfigurierbar) stoppt
    die gesamte Verteilung automatisch statt nur pro Gerät zu retryen.
  - Pro Gerät maximal x Wiederholungsversuche bei Fehlschlag, danach Blockade + Log + Admin-Info.
- Zufälliger Jitter (wenige Sekunden bis 1 Minute) vor dem tatsächlichen Update-Pull nach einem
  Boot-Call, um Lastspitzen bei gleichzeitigem Hochfahren vieler Geräte zu vermeiden.
- Update-Prüfung reicht beim Boot-Call, kein wiederkehrender Timer nötig — Verbreitung erfolgt
  automatisch bei jedem weiteren Boot eines noch nicht aktualisierten Geräts.

## 12. Install-Creator (separates Entwickler-Tool, nicht Teil der Kunden-Installation)
- Eigene ausführbare Datei im Projektverzeichnis, baut das komplette Produkt als Admin-Installer.
- Checkbox "Test-Installer": aktiviert → Installer-Passwort optional. Deaktiviert (regulärer
  Prod-Build) → Installer-Passwort Pflichtfeld, zusätzlich Eingabe eines Kundennamens Pflicht
  (daraus wird die Kunden-/Gruppen-ID abgeleitet, siehe Abschnitt 6).
- Alle Passwortfelder im Install-Creator und im Admin-Dashboard bekommen: einen
  "Zufällig generieren"-Button (Würfel-Icon), der eine 16–24-stellige Zufallszeichenkette
  erzeugt und einträgt, sowie ein "In Zwischenablage kopieren"-Icon direkt daneben.
- Passwörter werden **nirgends zentral gespeichert** — bewusste Entscheidung, um die
  Angriffsfläche klein zu halten. Bei Verlust wird ein neuer Installer gebaut.
- Admin-Installer hat einen Button "User-Installer erstellen": erzeugt den zugehörigen
  User-Installer mit derselben Kunden-/Gruppen-ID, optionales Installer-Passwort, das vor Ort
  vom jeweiligen System-Admin vergeben werden kann.
- **Nicht jetzt umsetzen** (bewusst zurückgestellt, siehe CLAUDE.md-Referenzdokumente-Hinweis):
  zeitlich befristete Demo-Installer. Wird eigenständig behandelt, sobald Licensing/Pricing
  final steht — an dieser Stelle nur als offener Punkt vermerken, keine Funktion dafür bauen.

## 13. Nicht Teil dieses Prompts
Die folgenden Punkte hängen von noch offenen externen Antworten ab und sollen **nicht** vorab
architektonisch festgelegt werden — modular genug bauen, dass sie sich später einfügen lassen,
aber nicht implementieren:
- Verhalten bei Mehrfach-VLAN/Mehrstandort-Netzen.
- Etwaige Integration in eine zentrale Softwareverteilung der Behörde (WSUS o. ä.) als
  Alternative zur P2P-Update-Verteilung.
