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
- `docs/handbuch/entwickler-alpha-testplan.md` — manuelle Checkliste für alles, was (noch) nicht coded ist
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
   Stadium. **→ dokumentiert, vorerst manuell (docs/handbuch/entwickler-alpha-testplan.md, Test 6).**

## Versions-Gates (wie im Chat vorgeschlagen, jetzt festgeschrieben)

| Version-Sprung | Was läuft |
|---|---|
| Patch (x.x.**Y**) | **Smoke-Set**: die mit 🔹 markierten Tests unten (~15 Stück, wenige Minuten) |
| Minor (x.**Y**.0) | **Volles coded Set**: alle `HaelpMi.Core.Tests` + alle `HaelpMi.Installer.Tests` |
| Major/1.0+ | Zusätzlich: FlaUI-UI-Suite (E2E auf PVE-VMs, siehe Abschnitt H) + Audio-Loopback-Suite, sobald gebaut |

Vor jedem Release zusätzlich: `docs/handbuch/entwickler-alpha-testplan.md` von Hand durchgehen (das schrumpft mit
der Zeit, siehe oben).

E2E: Wann neue Tests oder ein Lauf Pflicht sind und welches E2E-Gate je Versionssprung gilt, regelt
`AGENTS.md` Teil B, Abschnitt "E2E-Tests". Suite und Testfälle: siehe E2E-Suite
(`tests/HaelpMi.E2E.Tests`, PR #170).

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
| A6 🔹 | Silent-Deinstallation entfernt `{app}`, ProgramData, Dienste, Autostart-Task, Firewall-Regeln vollständig | `[Installer.Tests]` ✅ |
| A7 | Interaktive Deinstallation, Rückfrage mit "Nein" beantwortet: ProgramData/Einstellungen bleiben erhalten | `[Installer.Tests]` ✅ (steuert die native MsgBox) |
| A8 | Neuinstallation über behaltener Config: Raum-Seite wird übersprungen, DeviceId bleibt gleich | `[Installer.Tests]` ✅ |
| A9 🔹 | Admin- vs. User-Installer: korrekte Rolle, korrekte Startmenü-Verknüpfung, Firewall-Programmregel für den Agent bei beiden, für `HaelpMi.Config.exe` nur beim Admin | `[Installer.Tests]` ✅ |
| A10 | User-Installer-Export (Install-Creator → eingebettete Datei) trägt dieselbe CustomerGroupId wie der Admin-Installer | `[Installer.Tests]` ✅ |
| A11 🔹 | Zwei unabhängige CustomerGroupId-Installationen im selben Netz beeinflussen sich nie (Kreis-Isolation) | `HaelpMi.Core.Tests` ✅ (DiscoveryService/AlarmTcpListener) |
| A12 🔹 | Autostart-Task-Prinzipal ist nach frischer Installation BUILTIN\Users (nicht nur der installierende Admin-Account) - Regressionstest 11.08.2026, siehe `docs/handbuch/entwickler-alpha-testplan.md` Test 2 | ❌ **Nicht coded** — braucht ein zweites, nicht-lokal-admin Windows-Konto auf der Testmaschine, das `Installer.Tests`-Setup hat nur eines |
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
| B8 | Alarm-Feedback-Kanal ("bin unterwegs") Roundtrip, inkl. Primary/Satellite bei Fast User Switching (Issue #9-Nachtrag 2) | ✅ (26.08.2026, `AlarmFeedbackChannelTests.cs` - Regressionstest für live gemeldeten Bug: Sender-Gerät mit FUS bekam die Antwort nicht mit, Empfänger pingte endlos weiter) |
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
| D5 | Voller Swap-Pipeline-Durchlauf (Test-Port-Parallelinstanz, Peer-Bestätigung, Port-Übernahme, Alt-Deinstallation) | ❌ **Nicht coded, absichtlich** — siehe `docs/handbuch/entwickler-alpha-testplan.md` Test 9 ("nur auf Wegwerf-VMs"), zu riskant/aufwändig für automatisierte Ausführung im jetzigen Stadium |

## E. UI-Erreichbarkeit & Bedienung (geplant, noch nicht coded)

Ziel-Tool: [FlaUI](https://github.com/FlaUI/FlaUI) (.NET-natives UI-Automation-Wrapper,
robuster als die rohen Koordinaten-Klicks, die in dieser Sitzung für die Bug-Verifikation
improvisiert wurden). Aufbau erst, wenn ein Bereich strukturell 2+ Versionen stabil war.

| # | Test | Aktuell |
|---|---|---|
| E1 | Dashboard: jeder Tab öffnet ohne Fehler | Manuell (`docs/handbuch/entwickler-alpha-testplan.md` Test 3) |
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

## H. E2E (FlaUI auf PVE-VMs)

Projekt `tests/HaelpMi.E2E.Tests` (xUnit + FlaUI.UIA3), Workflow `.github/workflows/e2e.yml`.
Läuft auf bis zu drei Wegwerf-VMs auf pve-nzxt (A = Admin-Gerät, B und C = User-Geräte, Zuordnung in
`ci/e2e/geraete.json`), die vor jedem Lauf auf den Snapshot `clean-base` zurückgesetzt werden. Betrieb der Umgebung: `docs/betrieb.md`, Abschnitt
"E2E-Umgebung".

| # | Test | Gerät | FR-Bezug |
|---|---|---|---|
| T1 | Stille Installation (Admin- bzw. User-Installer): Programmordner, ProgramData, Rolle in `deployment.json`, unveränderte DeviceId/Raum in `settings.json`, Startmenü-Verknüpfung, geplante Aufgabe, Agent läuft; eingehende Firewall-Programmregel „HälpMi Agent“, nur A zusätzlich „HälpMi Konfiguration“, beide laut Setup-Log vor dem ersten HälpMi-Start angelegt; keine Inbound-Block-Regel mit HälpMi-Programmpfad (#222) | A, B, C | FR-3, FR-4, FR-34, FR-35, FR-37, NFR-2 |
| T2 | Verknüpfung "HälpMi Dashboard" öffnet das Admin-Dashboard ohne neuen `crash.log`-Eintrag | A | FR-4 |
| T3 | Selbsttest: Alarmfenster im Vordergrund mit "Raum (Nummer)", "Bin unterwegs", Schließen erst danach aktiv | A | FR-27, FR-40, FR-51 |
| T4 | Discovery: die Gegenseite steht nach spätestens 60 s mit Adresse in `devices.json` | A, B | – |
| T5 | Alarm per Hotkey von A an B: Banner auf A, Popup auf B mit Raum von A (Latenz wird gemessen), Rückmeldung von B, "Empfangen: 1 von 1" auf A | A, B | FR-40, FR-50, FR-51, FR-53 |
| T6 | Zwei Alarm-Profile über das Dashboard (UI-Automation): P1 A an B, Schwelle 1; P2 B an Gruppe mit A, Schwelle 2. Jeder Hotkey erreicht nur die eigenen Empfänger, Schwelle 2 hält Schließen gesperrt, bis B abbricht | A, B | FR-50, FR-51 |
| T7 | Config-Sync: A ändert Name, Schwelle und Hotkey von P2, B übernimmt ohne Neustart (Version, Audit-Zeile, "Meine Alarme"; Latenz wird gemessen), altes Kürzel wirkungslos, neues wirkt | A, B | – |
| T8 | Gerät B im Dashboard deaktivieren: B empfängt nicht (auch nicht bei Direktversand am Filter von A vorbei) und sendet nicht, Toast und Hinweis auf B; nach dem Aktivieren steht B in `devices.json` von A auf `LicenseOverride` None (#180) und beide Richtungen gehen wieder | A, B | – |
| T9 | Lizenz in fünf Stufen, Dateiwechsel jeweils mit gestoppten Agents auf beiden Geräten (sonst Übernahme per Gossip). L1 ohne Lizenz: A Erinnerungs-Toast, Banner, Bearbeitung gesperrt, Selbsttest geht; B Sperr-Toast, Hotkey nur mit Hinweis, Direktversand unbestätigt. L2 Import im Dashboard ohne Neustart: Toast weg, Bearbeitung frei, B übernimmt per Gossip (Latenz wird gemessen), Alarm A an B. L3 läuft in 10 Tagen ab: Warnung ohne Sperre, Alarm geht, "Später erinnern" hält über einen Neustart. L4 abgelaufen: Alarme in beide Richtungen (Soft-Expiry), A Toast und Sperre, B Hinweis-Toast „Lizenz … abgelaufen … Alarme weiter“ (#181). L5 gültige Lizenz zurück | A, B | – |
| T10 | Regression #136, `zyklen` Durchläufe (Default 25) mit eigenem Profil A an B (Schwelle 1, Config-Version 5000). Variante N: "Bin unterwegs" mit Versatz von -400 bis +400 ms zur nächsten Repeat-Welle (Phase aus den ms-Zeitstempeln "alarm received" in `audit.log` von B), sofort schließen, 8 s auf ein zweites Fenster achten. Variante W: A schickt nach dem Schließen dieselbe Session per `AlarmSender` noch einmal (Nachwelle), B beobachtet 8 s. Ergebnis je Zyklus in `out\r136-<Agent-Version>.csv`, Quoten in `r136-zusammenfassung.json`. Rot, wenn W ohne `produkt_ref` ein zweites Fenster zeigt; mit `produkt_ref` nur Messung | A, B | – |
| T11 | Discovery mit drei Geräten: A und B kennen C, C kennt A und B (je nach spätestens 60 s mit Adresse in `devices.json`) | A, B, C | – |
| T12 | Alarm per Hotkey von A an B und C mit Schwelle 2 (eigenes Profil, Config-Version 1100): Popup auf B und C mit Raum von A. B meldet "Bin unterwegs", Schließen bleibt auf B 5 s gesperrt, C zeigt "noch 1". Nach der Meldung von C wird Schließen auf beiden frei; A zeigt beide Räume unter "Auf dem Weg" und "Empfangen: 2 von 2". Danach beendet C seinen Agent | A, B, C | FR-47, FR-50, FR-51, FR-53 |
| T13 | Regression #182: eigenes Profil A an B (Schwelle 1, Config-Version 6000), kein "Bin unterwegs". Nach "Empfangen: 1 von 1" auf A beendet B seinen Agent, A beobachtet das Banner 15 s lang. Rot, wenn trotz mindestens einer weiteren, unquittierten Welle (`audit.log` von A) ein anderer Text als "Empfangen: 1 von 1" erscheint; danach startet B den Agent wieder | A, B | FR-14, FR-53 |
| T14 | Regression #183: A mit `lizenz.json`, B mit `lizenz-abgelaufen-neuer.json` (später ausgestellt, abgelaufen); B startet nach A neu, damit Boot-Call und Antwort die Lizenzen austauschen. A behält die gültige Lizenz (kein "Lizenz von Peer" im `audit.log`), B übernimmt sie binnen 60 s. Danach beide wieder `lizenz.json` | A, B | – |
| T15 | Regression #133: A deaktiviert B im Dashboard, das Banner "Lizenzkontingent überschritten" bleibt 10 s aus; B zeigt den Sperr-Toast mit Grund "Gerät deaktiviert" (Titel und Text je Sperrgrund aus `LicenseBlockTexts`). A aktiviert B danach in einem neu geöffneten Dashboard wieder, der Toast auf B ist weg | A, B | – |
| T16 | Regression #180: A deaktiviert B im Reiter "Geräte / Lizenz", wartet, bis B `ForceDisabled` übernommen hat, und aktiviert es wieder. Rot, wenn B danach in `devices.json` von A nicht auf `LicenseOverride` None steht (vor dem Fix: ForceEnabled, B umgeht dann das Kontingent). B prüft, dass es None per Gossip in `settings.json` übernimmt | A, B | – |
| T17 | Regression #181: A und B bekommen aus `lizenz-kurz/` die Lizenz, die als nächste, aber frühestens 3 min später abläuft (gleiche Ausstellungszeit wie `lizenz.json`). A schließt den Start-Toast „läuft in 0 Tag(en) ab“. Rot, wenn ohne Agent-Neustart bis 60 s nach dem Ablauf auf A kein Toast „Lizenz seit … abgelaufen“ bzw. auf B kein Hinweis „… abgelaufen … Alarme weiter …“ erscheint. Danach beide wieder `lizenz.json` | A, B | – |
| T18 | Regression #136 mit drei Geräten, `zyklen` Durchläufe: A alarmiert B und C mit Schwelle 2 (eigenes Profil, Config-Version 1200). B und C klicken "Bin unterwegs" wie in T10 Variante N mit demselben Versatz von -400 bis +400 ms zur nächsten Repeat-Welle (je eigene Wellenphase), schließen sofort, sobald frei, und achten 8 s auf ein zweites Fenster; keine Nachwelle. Läuft nach einem Neustart eines Empfängers der Alarm noch, bricht A ihn ab. Ergebnis je Empfänger in `out\r136-drei-<b\|c>-<Agent-Version>.csv` und `r136-drei-<b\|c>-zusammenfassung.json`. Rot ohne `produkt_ref`, wenn ein zweites Fenster erscheint, Schließen nicht frei wird oder das Fenster offen bleibt; mit `produkt_ref` nur Messung. Danach beendet C seinen Agent wieder | A, B, C | FR-47, FR-50, FR-51 |
| T19 | Regression #136 "Mehrfach-Nachwelle", `zyklen` Durchläufe, Trait `Teil=MehrfachNachwelle` (zusätzlich zu `Teil=DreiGeraete`): A alarmiert B und C mit Schwelle 2 (eigenes Profil, Config-Version 1300). B und C klicken sofort "Bin unterwegs" und "Schließen", sobald frei. Danach schickt A per `AlarmSender` 3 bis 5 weitere Wellen derselben Session an beide, jeweils nach 0,5 bis 2 s (zufällig, pro Zyklus reproduzierbar). B klickt jedes wieder erscheinende Fenster sofort erneut weg ("Bin unterwegs", "Schließen", wie beim Kunden), C zählt nur. Gezählt wird je Zyklus und Empfänger die Zahl der verschiedenen Alarmfenster (Fenster-Handles, erwartet 1) und, ob "Bin unterwegs" wieder aktiv war (Rücksetzung); beobachtet wird bis 3 s nach der letzten Nachwelle. Ergebnis in `out\r136-mehrfach-<b\|c>-<Agent-Version>.csv` und `r136-mehrfach-<b\|c>-zusammenfassung.json`. Rot ohne `produkt_ref` bei mehr als einem Fenster, einer Rücksetzung, nie freiem Schließen, offen gebliebenem Fenster oder wenn in einem Zyklus keine Nachwelle ankam (`alarm received` im `audit.log`); mit `produkt_ref` nur Messung. Danach beendet C seinen Agent wieder | A, B, C | FR-50, FR-51 |
| T20 | Regression #177, Wiederholungstakt (Trait `Bereich=R177`): eigenes Profil A an B (Schwelle 1, Config-Version 6100), B startet seinen Agent vorher neu, der Alarm ist also sein erster Empfang. B misst die Abstände der ersten drei Wellen (`alarm received` im `audit.log`) und die Dauer einer UI-Automation-Abfrage am frisch geöffneten Alarmfenster, beendet dann seinen Agent. A beobachtet danach 20 s lang die Wellen ohne Quittung (`alarm sent`). Rot, wenn ein Abstand über 6 s liegt, A in 20 s weniger als 3 Wellen sendet oder die UIA-Abfrage länger als 2 s dauert. Ergebnisse in `out\t20-takt-a.txt` und `out\t20-takt-b.txt`. Vor dem Fix: erste Welle nach Agent-Start 6,3–6,8 s, ohne Quittung 15 s (Quittungs-Timeout + Relay-Timeout + Pause) | A, B | FR-50 |
| T21 | Regression #176 (Trait `Bereich=R176`, Priorität 25, direkt nach T1): Selbsttest auf A ohne Alarm-Profil. Erwartet eine Meldung des Einstellungsfensters mit Grund („Alarm-Profil“) und Lösungsweg („Dashboard“), kein Alarmfenster; „Ja“ öffnet das Admin-Dashboard. Rot, wenn keine Meldung erscheint (vor dem Fix: nichts passiert) | A | FR-27 |
| T24 | Regression #222: vor einer Reparatur-Installation des Admin-Installers zwei lokale Inbound-Block-Regeln anlegen, eine für `HaelpMi.Agent.exe` im Programmordner, eine Kontrollregel für eine gleichnamige `.exe` außerhalb. Nach der Reparatur ist die erste weg, die Kontrollregel noch da, die Allow-Regeln „HälpMi Agent“ und „HälpMi Konfiguration“ sind vorhanden, das Setup-Log nennt „Block-Regel entfernt: …“ nur für die erste. Läuft zuletzt (die Reparatur startet den Agent neu), braucht T1 | A | – |

T4 bis T10, T13 bis T17 und T20 (Trait `Teil=ZweiGeraete`) laufen nur, wenn A und B beteiligt sind, T11, T12, T18 und T19
(`Teil=DreiGeraete`) nur mit A, B und C. Die Geräte stimmen sich über Markendateien ab, die
`ci/e2e/rendezvous-relay.sh` zwischen allen beteiligten VMs spiegelt. T11/T12/T18/T19 laufen zwischen T5 und T6
(Priorität 53/56/57/58). Ein Lauf mit `a,b` ist dadurch unverändert. C beendet nach T12, T18 und T19 seinen
Agent, damit es ab T6 nicht mehr mitspielt (T18 und T19 starten ihn über die Profilvorbereitung neu): sonst verteilte es in T9 seine gültige Lizenz per Gossip an A und B
(`TryAdoptFromPeer`). Trait `Bereich`:
`Haupt` (T6 bis T9) setzt Installation, Discovery und Alarm voraus (`Basis`: T1, T4, T5, T11, T12), ein
Teillauf filtert deshalb z. B. auf `Bereich=Basis|Bereich=Haupt`. `R136` (T10, T18, T19), `R182` (T13), `R177` (T20), `R183` (T14), `R133` (T15), `R180` (T16), `R181` (T17) und `R222` (T24) brauchen nur `Basis`.

Auslöser: Label `e2e` an einem PR aus diesem Repo (nicht aus Forks) oder `workflow_dispatch`
(Input `vms`: Geräte aus `ci/e2e/geraete.json`, z. B. `a,b` oder `a,b,c`; Default `a,b,c`). Beim
Label-Lauf gilt `VMS=` aus `ci/e2e/lauf.env`, leer = alle Geräte. Kein Zeitplan (Entscheidung E6 in
#163: der Test-Host läuft nicht dauerhaft).

Laufparameter (Inputs von `workflow_dispatch`, beim Label-Lauf aus `ci/e2e/lauf.env` im PR-Branch,
weil `workflow_dispatch` nur Workflows vom Default-Branch startet):

- `produkt_ref` / `PRODUKT_REF`: Tag oder voller Commit-SHA, aus dem Agent, Konfiguration und
  Installer gebaut werden (zweiter Checkout nach `produkt/`). Die Tests und die Testlizenz-Erzeugung
  kommen immer vom PR-Stand. Leer = PR-Stand.
- `filter` / `FILTER`: wird als `&(…)` an den `dotnet test`-Filter gehängt.
- `zyklen` / `ZYKLEN`: Durchläufe für T10, T18 und T19, 1 bis 100, Default 25 (T10 etwa 50 s je Zyklus,
  T18 ohne Nachwelle weniger, T19 etwa 20 s, mit Agent-Neustart nach einem Treffer etwa 40 s). T10 und T18 mit je 100 Zyklen passen nicht zusammen in das Zeitlimit eines
  Test-Jobs (120 min); dafür zwei Läufe: T10 mit `VMS=a,b` (T18 entfällt ohne C), T18 mit `VMS=a,b,c` und
  `FILTER=Bereich=Basis|(Bereich=R136&Teil=DreiGeraete&Teil!=MehrfachNachwelle)` (ohne den letzten Teil läuft
  T19 mit). T19 allein: `VMS=a,b,c`, `FILTER=Bereich=Basis|(Bereich=R136&Teil=MehrfachNachwelle)`; 100 Zyklen
  passen in das Zeitlimit.
- `vms` / `VMS`: Geräteauswahl, geprüft im Job `plan` (`ci/e2e/geraete.sh`); aus ihr entstehen die
  Matrix der Test-Jobs, die VMIDs für Reset und Stopp und die Argumente des Relays.

Alle Werte werden per Regex geprüft, sonst bricht der Build-Job ab. Vergleichslauf #136 gegen den
Stand vor dem Fix: in `ci/e2e/lauf.env` `PRODUKT_REF=v0.46.1` und `FILTER=Bereich=Basis|Bereich=R136`
(mit `VMS=a,b,c` auch T18) committen, Label `e2e` setzen, danach die Datei wieder leeren.

Gate-Einordnung: verbindlich ist `AGENTS.md` Teil B, Abschnitt "E2E-Tests" (Patch: betroffene
Bereiche, sofern E2E-relevant; Minor: volle Suite `a,b`; Major/1.0+: volle Suite inkl.
Regressionsfälle wie T10, `Bereich=R136`). Mit C bedeutet „volle Suite“ `a,b,c` (Default).

Bekannte Umgehungen in der Testvorbereitung (keine Produktkorrektur):

- **Eigene Kundengruppe und Testlizenz pro Lauf** (`ci/e2e/testlizenz-erzeugen.ps1`): Die
  Lizenz ist an die Kundengruppe gebunden (ohne `/DCustomerGroupId` bekäme der Installer eine
  eigene Zufallsgruppe, siehe `installer/HaelpMiKundengruppe.iss.inc`); ohne gültige Lizenz hat ein User-Gerät Kontingent 0.
  Der Workflow erzeugt deshalb pro Lauf Gruppe, Schlüsselpaar und signierte Lizenz wie der
  Install-Creator für einen Test-Installer. Der private Schlüssel existiert nur im Speicher des
  Build-Jobs. Mit demselben Schlüssel entstehen für T9 `lizenz-bald.json` (läuft in 10 Tagen ab),
  `lizenz-abgelaufen.json`, für T14 `lizenz-abgelaufen-neuer.json` und für T17 unter `lizenz-kurz/` Lizenzen, die gestaffelt alle 5 min bis 8 h nach dem Build ablaufen (der private Schlüssel ist zur Testzeit nicht mehr da); `lizenz.json` gilt 365 Tage, weil ab 30 Tagen Restlaufzeit schon der
  Erinnerungs-Toast erscheint.

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
  komplett, auch als manueller Testfall. Ergänzt in `docs/handbuch/entwickler-alpha-testplan.md` sinnvoll.
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
