# HälpMi Test-Strategie

Dieses Dokument ist die Antwort auf die Nutzerfrage vom 06.08.2026 ("build ui tests for
every mandatory function... is this feasible or too much?") und ersetzt keine der
bestehenden Dateien, sondern verbindet sie:

- `tests/HaelpMi.Core.Tests/` — coded, läuft bei jedem `dotnet test`, keine Systemänderung.
- `tests/HaelpMi.Installer.Tests/` — coded, **verändert das ausführende System** (Program
  Files, ProgramData, Dienste, Registry, Firewall-Regeln). Siehe Warnhinweis dort.
- `tests/HaelpMi.Audio.Tests/` — coded, **spielt hörbar den echten Alarmton auf jedem
  aktiven Wiedergabegerät ab** (seit 13.08.2026 aus `HaelpMi.Core.Tests` ausgelagert,
  siehe README dort). Läuft ebenfalls nicht automatisch bei jedem `dotnet test`.
- `ALPHA-TESTPLAN.md` — manuelle Checkliste für alles, was (noch) nicht coded ist
  (UI-Erreichbarkeit, Ton, erzwungener Vordergrund). Wird schrittweise kleiner, je mehr
  hier automatisiert wird.

## Warum nicht "alles jetzt automatisieren"

"UI-Tests" wurde in der Anfrage für drei sehr unterschiedliche Testarten benutzt:

1. **Installer-/Lebenszyklus-Tests** (Update über laufende Installation, Deinstallation,
   Neuinstallation über behaltener Config, User-Installer-Export) — keine Bildschirm-
   Interaktion nötig, nur stiller Installer-Aufruf + Datei-/Registry-/Dienst-Prüfung.
   Deterministisch, günstig, ändert sich kaum. **→ jetzt coded (dieses Update).**
2. **Kernlogik** (Config-Sync, Empfänger-Auflösung, Geräte-Abgleich, Nachrichten-
   Validierung) — größtenteils schon in `HaelpMi.Core.Tests` (111 Tests). **→ läuft
   bereits, wird bei jedem neuen Feature erweitert.**
3. **Echte WPF-UI-Automatisierung** (Dashboard-Buttons klicken, Popup-Inhalt prüfen) —
   technisch machbar (z. B. mit FlaUI statt der rohen Koordinaten-Klicks von heute), aber
   die Oberfläche verändert sich gerade wöchentlich strukturell (Sender/Empfänger-Grid,
   Tray-Menü, Plus-Button - alles in dieser einen Sitzung umgebaut). Jetzt geschrieben,
   wären das größtenteils Tests, die beim nächsten Layout-Wunsch wieder kaputt sind.
   **→ Testfälle unten dokumentiert, Umsetzung erst wenn die Oberfläche sich beruhigt hat
   (Zielmarke: ab 1.0 oder wenn ein Bereich seit 2+ Versionen strukturell stabil war).**
4. **Ton-Wiedergabe-Prüfung** ("Ton wird wirklich abgespielt", "auch bei stummgeschaltetem
   Gerät") — braucht eine virtuelle Audio-Loopback-Aufnahme, um wirklich zu verifizieren
   statt zu vermuten. Eigene Infrastruktur, hoher Aufwand gegenüber Nutzen im Alpha-
   Stadium. **→ dokumentiert, vorerst manuell (ALPHA-TESTPLAN.md, Test 6).**

## Versions-Gates (wie im Chat vorgeschlagen, jetzt festgeschrieben)

| Version-Sprung | Was läuft |
|---|---|
| Patch (x.x.**Y**) | **Smoke-Set**: die mit 🔹 markierten Tests unten (~15 Stück, wenige Minuten) |
| Minor (x.**Y**.0) | **Volles coded Set**: alle `HaelpMi.Core.Tests` + alle `HaelpMi.Installer.Tests` |
| Major/1.0+ | Zusätzlich: FlaUI-UI-Suite + Audio-Loopback-Suite, sobald gebaut (siehe unten) |

Vor jedem Release zusätzlich: `ALPHA-TESTPLAN.md` von Hand durchgehen (das schrumpft mit
der Zeit, siehe oben).

---

## A. Identität & Deployment (Installer-Lebenszyklus)

Alle mit `[Installer.Tests]` sind ab jetzt coded — siehe Projekt-README dort für den
Ausführungs-Hinweis (verändert das System, läuft NICHT automatisch in jedem `dotnet test`).

| # | Test | Status |
|---|---|---|
| A1 🔹 | Frische Installation schreibt gültige `settings.json` (DeviceId-GUID-Format, Raum/-nummer, Rolle) | `[Installer.Tests]` ✅ |
| A2 🔹 | Frische Installation schreibt gültige `deployment.json` (CustomerGroupId, Role, IsTestInstaller) | `[Installer.Tests]` ✅ |
| A3 | Erneuter Lauf derselben Version = Reparatur, DeviceId bleibt unverändert | `[Installer.Tests]` ✅ |
| A4 | Downgrade wird verweigert (silent UND interaktiv) | `[Installer.Tests]` ✅ |
| A5 🔹 | Update über laufende Installation: alte Prozesse beendet, neue Dateien da, danach genau EIN Agent-Prozess, Ports binden ohne Kollision | `[Installer.Tests]` ✅ |
| A6 🔹 | Silent-Deinstallation entfernt `{app}`, ProgramData, Dienste, Autostart-Task, (Admin) Firewall-Regeln vollständig | `[Installer.Tests]` ✅ |
| A7 | Interaktive Deinstallation, Rückfrage mit "Nein" beantwortet: ProgramData/Einstellungen bleiben erhalten | `[Installer.Tests]` ✅ (steuert die native MsgBox) |
| A8 | Neuinstallation über behaltener Config: Raum-Seite wird übersprungen, DeviceId bleibt gleich | `[Installer.Tests]` ✅ |
| A9 🔹 | Admin- vs. User-Installer: korrekte Rolle, korrekte Startmenü-Verknüpfung, Firewall-Regeln nur beim Admin | `[Installer.Tests]` ✅ |
| A10 | User-Installer-Export (Install-Creator → eingebettete Datei) trägt dieselbe CustomerGroupId wie der Admin-Installer | `[Installer.Tests]` ✅ |
| A11 🔹 | Zwei unabhängige CustomerGroupId-Installationen im selben Netz beeinflussen sich nie (Kreis-Isolation) | `HaelpMi.Core.Tests` ✅ (DiscoveryService/AlarmTcpListener) |
| A12 🔹 | Autostart-Task-Prinzipal ist nach frischer Installation BUILTIN\Users (nicht nur der installierende Admin-Account) - Regressionstest 11.08.2026, siehe `ALPHA-TESTPLAN.md` Test 2 | ❌ **Nicht coded** — braucht ein zweites, nicht-lokal-admin Windows-Konto auf der Testmaschine, das `Installer.Tests`-Setup hat nur eines |
| A13 | Swap-Update repariert einen fehlenden/veralteten Autostart-Task als Nebeneffekt (LocalSystem-Kontext von `HaelpMi.UpdateService`, greift für vor diesem Fix installierte Geräte) | ❌ **Lücke** — `AutostartRegistrar.EnsureRegistered`-Aufruf in `ConfirmSwapAsync` ist neu, noch nicht durch A5 mitabgedeckt |

## B. Netzwerk / Discovery

| # | Test | Status |
|---|---|---|
| B1 🔹 | Boot-Call Announce+Reply entdeckt ein neues Gerät automatisch | ✅ |
| B2 | Gossip: bekannte Geräte werden über eine Reply mit übernommen | ✅ |
| B3 | Geräte-Abgleich über DeviceId, nicht IP | ✅ |
| B4 | Config-Sync Hot-Reload verteilt eine Änderung ohne Neustart | ✅ (`ConfigSyncService`-Roundtrip, siehe `NetworkingTests`) |
| B5 | Exklusiv-Edit-Lock: Kollision → beide lehnen ab → zufälliger Backoff → Retry | ❌ **Lücke** — nur der Port-belegt-Fall ist getestet, nicht die eigentliche Lock-Logik |
| B6 | Edit-Lock Auto-Freigabe nach 10 Min. Inaktivität | ❌ **Lücke** |
| B7 🔹 | Alarm-Anfrage/Ack Roundtrip über TCP | ✅ |
| B8 | Alarm-Feedback-Kanal ("bin unterwegs") Roundtrip | ❌ **Lücke** — nur Port-belegt-Fall getestet |
| B9 🔹 | Alle 4 TCP-Dienste degradieren bei belegtem Port ohne Absturz | ✅ (dieser Sitzung's Regressions-Tests) |
| B10 🔹 | Repeating-Alarm-Session stoppt bei Erreichen des PRO PROFIL konfigurierten Schwellwerts (nicht eines festen Default) | ✅ (07.08.2026, Regressionstest für live gemeldeten Bug - siehe SendingTests.cs) |
| B11 | Multi-VLAN-Bridge-Seed: AnnounceAsync unicastet zusätzlich zum lokalen Broadcast an eine konfigurierte Bridge-Seed-Adresse | ✅ (13.08.2026, siehe NetworkingTests.cs) |
| B12 | Multi-VLAN-Bridge-Seed: ein über die Brücke neu gelerntes Gerät löst einen sofortigen lokalen Re-Announce aus (statt erst beim nächsten eigenen Boot) | ✅ (13.08.2026, Regressionsschutz - gleiche Fehlerklasse wie der PeerConfigVersionObserved-Fix vom 11.08.2026) |
| B13 | Multi-VLAN-Bridge-Seed: ein reiner Refresh eines bereits bekannten Geräts löst KEINEN zusätzlichen Re-Announce aus | ✅ (13.08.2026, Gegenprobe zu B12) |

## C. Konfigurationslogik

| # | Test | Status |
|---|---|---|
| C1 🔹 | Direktes Gerät als Sender → direkter Empfänger | ✅ |
| C2 🔹 | Gruppen-Sender/-Empfänger-Auflösung | ✅ |
| C3 🔹 | Raum-basierte Sender/Empfänger (gleiche Raumnummer) | ✅ |
| C4 | Empfänger-Zählung ohne Doppelzählung bei Mehrfach-Erreichbarkeit | ✅ |
| C5 | Save-on-Blur: `PublishAsync` speichert+verteilt sofort, kein Save-Button nötig | ❌ **Lücke** — nur die reine Auflösungslogik ist getestet, nicht der Publish/Broadcast-Zyklus selbst |
| C6 | Änderungshistorie + Undo pro Gruppe/Profil | ❌ **Lücke** |

## D. Update-Pipeline

| # | Test | Status |
|---|---|---|
| D1 🔹 | Signaturprüfung: akzeptiert/verwirft (manipuliert, falscher Schlüssel, kaputtes Base64) | ✅ |
| D2 🔹 | Versionsvergleich numerisch, nicht als String | ✅ |
| D3 | Gestaffelter Rollout: Quote/Reihenfolge nach stabiler DeviceId | ✅ |
| D4 | Lockout nach N aufeinanderfolgenden Fehlschlägen | ❌ **Lücke** |
| D5 | Voller Swap-Pipeline-Durchlauf (Test-Port-Parallelinstanz, Peer-Bestätigung, Port-Übernahme, Alt-Deinstallation) | ❌ **Nicht coded, absichtlich** — siehe `ALPHA-TESTPLAN.md` Test 9 ("nur auf Wegwerf-VMs"), zu riskant/aufwändig für automatisierte Ausführung im jetzigen Stadium |

## E. UI-Erreichbarkeit & Bedienung (geplant, noch nicht coded)

Ziel-Tool: [FlaUI](https://github.com/FlaUI/FlaUI) (.NET-natives UI-Automation-Wrapper,
robuster als die rohen Koordinaten-Klicks, die in dieser Sitzung für die Bug-Verifikation
improvisiert wurden). Aufbau erst, wenn ein Bereich strukturell 2+ Versionen stabil war.

| # | Test | Aktuell |
|---|---|---|
| E1 | Dashboard: jeder Tab öffnet ohne Fehler | Manuell (`ALPHA-TESTPLAN.md` Test 3) |
| E2 | Neues Profil ist sofort editierbar (Name/Text/Hotkey/Schwellwert befüllt) | Heute per Hand mit Screenshots verifiziert, siehe Chat-Verlauf 06.08. — guter erster FlaUI-Kandidat |
| E3 | Save-on-Blur aktualisiert sichtbar UND bleibt nach Fenster-Neustart erhalten | Manuell |
| E4 | Sender/Empfänger-Zuordnung per Klick | Manuell |
| E5 | Popup: erzwungener Vordergrund, auch über Vollbild-Anwendungen | Manuell (am wenigsten automatisierbar - Vollbild-Interaktion) |
| E6 | Popup: Raum/Raumnummer groß, Benutzername klein (Datenschutz-Vorgabe) | Manuell — **könnte aber schon jetzt günstig als reiner XAML/FontSize-Vergleich coded werden**, kein Live-UI nötig |
| E7 | Popup: "Schließen" bleibt bis Schwellwert-Erreichen deaktiviert | Manuell |
| E8 | Tray-Menü zeigt "Dashboard öffnen" nur bei Admin-Rolle | Manuell (heute per Screenshot stichprobenartig geprüft) |
| E9 | "Empfängerliste übertragen" nur aktivierbar, wenn gewählter Sender mindestens einen Empfänger hat | 07.08.2026 per Screenshot live verifiziert, siehe Chat-Verlauf — guter FlaUI-Kandidat |
| E10 | Sender-Auswahl bleibt erhalten, wenn währenddessen ein Empfänger (de-)markiert wird | 07.08.2026 live gefundener und gefixter Bug (WPF `ItemsSource=null`-Reload-Race), per Screenshot verifiziert - **hoher Regressions-Wert, da die Ursache strukturell ist (jeder ComboBox/ListBox-Reload-Reset), nicht nur dieser eine Klickpfad** |
| E11 | ConfigWindow "Meine Alarme" zeigt ein Profil, bei dem das eigene Gerät (direkt/Gruppe/Raum) selbst zu seinen Empfängern zählt | 08.08.2026 live gefundener und gefixter Bug (`ConfigWindowContext` fehlte `LoadOwnDevice`, das eigene Gerät fiel beim finalen Geräteliste-Abgleich in `RecipientResolver.ResolveRecipientsForSender` heraus - Profil verschwand komplett aus der Liste), per Screenshot verifiziert. Der WPF-Wiring-Teil selbst bleibt ungetestet (siehe Abschnitt "Warum nicht alles jetzt automatisieren"), aber die darunterliegende Resolver-Annahme ist seit 11.08.2026 durch drei coded Tests abgesichert (`RecipientResolver_IncludesSenderDevice_*`, `HaelpMi.Core.Tests/ModelsTests.cs`) |

## F. Ton-Wiedergabe (geplant, noch nicht coded)

Braucht eine virtuelle Audio-Loopback-Senke (z. B. VB-Cable) zur Aufnahme+Analyse, sonst
lässt sich "wurde wirklich etwas hörbar abgespielt" nicht von "NAudio hat keine Exception
geworfen" unterscheiden.

| # | Test | Aktuell |
|---|---|---|
| F0 🔹 | `PlayOnAllActiveDevicesAsync` kehrt in begrenzter Zeit zurück, hängt sich nie auf | ✅ (07.08.2026, `[Audio.Tests]` - Regressionstest für live gefundenen Hang: NAudios `SampleToWaveProvider` + `WasapiOut.Dispose()` blieben auf mind. einer Testumgebung unbegrenzt hängen, siehe Kommentar in `MultiDeviceAlarmPlayer.cs`. Seit 13.08.2026 in einem eigenen Projekt statt in `HaelpMi.Core.Tests` - spielt hörbar echten Ton ab, siehe README dort) |
| F1 | Ton wird beim Alarmempfang tatsächlich HÖRBAR abgespielt (Loopback-Aufnahme zeigt Audiopegel) | Manuell — F0 prüft nur "hängt nicht/wirft nicht", nicht "war wirklich etwas zu hören" |
| F2 | Ton wird auch bei stummgeschaltetem Standard-Windows-Ausgabegerät abgespielt (Mehrgeräte-Ausgabe, NAudio) | Manuell — der unsicherste Punkt im ganzen Testplan |
| F3 | Korrekter Ton je `IncomingSoundId` wird gewählt | Teilweise coded (`IncomingSoundCatalog_Resolve_FallsBackToFirstOption_ForUnknownId`), Wiedergabe selbst nicht |

## G. Datenschutz/Sicherheit (Randnotiz, kein eigener Testlauf nötig)

- RDP-Sitzungskennzeichnung wird nirgends langfristig protokolliert außer im ohnehin
  vorgesehenen Alarm-Log — architektonisch sichergestellt (kein separater Store dafür
  existiert), kein Laufzeit-Test nötig, nur bei jedem neuen Feature im Review prüfen.
- Keine Secrets/Schlüssel im Code/Log/Fehlermeldung — bereits Teil des in CLAUDE.md
  festgelegten Gewohnheits-Checks vor jedem Feature-Abschluss, kein separater Testlauf.

---

## Was beim ursprünglichen Wunsch noch fehlte (Antwort auf "did I forget something")

- **Edit-Lock-Kollision/Backoff** (B5/B6) — echte Konkurrenz-Situation, bisher ungetestet.
- **Firewall-Regeln** — jetzt in `[Installer.Tests]` (A9) abgedeckt.
- **Kreis-Isolation** — bereits als A11 vorhanden, aber es lohnt sich, das explizit als
  "nicht verhandelbar" (CLAUDE.md) zu markieren, nicht nur als Detail.
- **Interop während laufender automatischer Weiterverteilung** (alte + neue Version
  gleichzeitig im Netz, solange noch nicht jedes Gerät gebootet/gezogen hat) — fehlt
  komplett, auch als manueller Testfall. Ergänzt in `ALPHA-TESTPLAN.md` sinnvoll.
- **Update-Signatur-Ablehnung End-to-End** (nicht nur die reine Kryptoprüfung) — D5 deckt
  das konzeptionell ab, ist aber bewusst nicht automatisiert (siehe oben).
- **CrashLogger-Zuverlässigkeit** — ob ein echter Crash wirklich geloggt wird, wurde diese
  Sitzung nur indirekt (Windows Event Log) verifiziert, nie der eigene Log-Pfad selbst.
- Lizenz-/Soft-Expiry-Prüfung ist noch nicht gebaut (nur der öffentliche Schlüssel liegt
  vor) - keine Testlücke, sondern schlicht noch kein Feature.

## Nächste Schritte

1. `tests/HaelpMi.Installer.Tests/` ist ab jetzt vorhanden — vor dem nächsten Release
   einmal auf einer Wegwerf-VM laufen lassen (siehe README dort, **nicht auf einer
   Maschine mit echten Daten** - das Setup installiert/deinstalliert wirklich).
   Ich habe es NICHT gegen diese geteilte Session-Maschine laufen lassen, weil das die
   echte Installation/Config hier (Raum "Mission Control" etc.) mit-löschen würde.
2. B5/B6/C5/C6/D4 als nächste Core.Tests-Lücken schließen (klein, kein Systemzugriff nötig).
3. E6 (Popup-Textgrößen) ist der günstigste erste UI-Test - kein Live-Fenster nötig, nur
   die Style-Ressourcen selbst prüfen.
4. FlaUI-Suite erst aufsetzen, wenn Dashboard/Popup-Layout sich beruhigt haben.
