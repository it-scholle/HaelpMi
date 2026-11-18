# Design-System – Katalog

Jedes Oberflächen-Element existiert genau einmal in diesem Verzeichnis und wird nur referenziert
(Regeln: `AGENTS.md`, Teil A „Design-System“ und Teil B „Design-System“). Vor dem Anlegen eines
neuen Elements hier nachsehen.

## Aufbau

| Pfad | Inhalt | Eingebunden in |
|---|---|---|
| `icons/` | App-Logo in allen Formaten | `ApplicationIcon` (Agent, Config, InstallCreator), `Resource` mit `Link` (UI, InstallCreator), `SetupIconFile` (beide Installer) |
| `xaml/Design.xaml` | Farben (`…Color`, `…Brush`) und Schrift | per `<Page Include=… Link="Styles\Design.xaml">` in HaelpMi.UI und HaelpMi.InstallCreator, gemergt in deren `Styles/ModernStyles.xaml` |

Änderungen hier lösen auch den Rebuild des Install-Creators aus (`.githooks/lib/rebuild-installcreator.sh`).

## Logo

| Name | Bedeutung | Bausteine | Quelle | Lizenz | Textalternative |
|---|---|---|---|---|---|
| `icons/haelpmi_icon.svg` | App-Logo, Vektorquelle (blaues Quadrat mit konzentrischen Signalringen) | – | eigenes Design | HälpMi-Lizenz (`LICENSE`) | „HälpMi“ |
| `icons/haelpmi_icon.ico` | App-, Installer- und Fenster-Icon (16, 32, 48, 256 px) | aus `haelpmi_icon.svg` | eigenes Design | HälpMi-Lizenz | „HälpMi“ |
| `icons/haelpmi_icon_256.png` | Logo als Bild, 256 px | aus `haelpmi_icon.svg` | eigenes Design | HälpMi-Lizenz | „HälpMi“ |
| `icons/haelpmi_icon_512.png` | Logo als Bild, 512 px (früher auch als ungenutztes `splash.png` dupliziert) | aus `haelpmi_icon.svg` | eigenes Design | HälpMi-Lizenz | „HälpMi“ |
| `icons/haelpmi_icon_1024.png` | Logo als Bild, 1024 px | aus `haelpmi_icon.svg` | eigenes Design | HälpMi-Lizenz | „HälpMi“ |

Offen: Die `.ico` enthält keine 20-, 24-, 40- und 64-px-Stufen; Windows skaliert dafür aus der
nächsten Größe. Bei Unschärfe in Taskleiste oder Startmenü aus der SVG ergänzen.

## Icons (Lucide)

Quelle: `lucide-static` v0.460.0, Lizenz ISC (`icons/lucide/LICENSE`). Die SVGs liegen unverändert
in `icons/lucide/`; die Pfade sind als `Geometry`-Ressourcen `Icon.*` in `xaml/Design.xaml`
übernommen (24er-Raster, absolute Koordinaten, Kreise und Rechtecke als Pfade). Darstellung immer
über den Style `LucideIconStyle` (Linienstärke 2 im 24er-Raster, runde Enden, Farbe = Foreground):

```xml
<ContentControl Style="{StaticResource LucideIconStyle}" Tag="{StaticResource Icon.X}" Width="16" Height="16" />
```

Icons sind Dekoration. Ein Button, der nur ein Icon zeigt, braucht `AutomationProperties.Name`
(Textalternative) und einen `ToolTip`.

| Name | Ressource | Bedeutung | Verwendet in | Ersetzt | Textalternative |
|---|---|---|---|---|---|
| `icons/lucide/x.svg` | `Icon.X` | schließen, entfernen | Schließen-Buttons aller Toasts und des Sender-Banners, „Gerät löschen“ | Zeichen ✕, gedrehte Rechtecke, eigener Pfad | „Schließen“ bzw. „Gerät löschen“ |
| `icons/lucide/check.svg` | `Icon.Check` | erfolgreich erledigt | Download-Toast, Build-Erfolgs-Toast | Zeichen ✓ | – (dekorativ, Titeltext daneben) |
| `icons/lucide/chevron-down.svg` | `Icon.ChevronDown` | Auswahlliste aufklappen | ComboBox-Template | gefülltes Dreieck | – (Teil der ComboBox) |
| `icons/lucide/triangle-alert.svg` | `Icon.TriangleAlert` | Alarm/Warnung | Kopf des Alarm-Popups | Zeichen ⚠ | – (dekorativ, „HälpMi - Alarm“ daneben) |
| `icons/lucide/volume-2.svg` | `Icon.Volume2` | Ton wird abgespielt | Alarm-Popup, Hinweis zum Signalton | Emoji 🔊 | – (dekorativ, Text daneben) |
| `icons/lucide/plus.svg` | `Icon.Plus` | neu anlegen | „Neues Alarm-Profil“ | zwei Rechtecke | „Neues Alarm-Profil“ |
| `icons/lucide/minus.svg` | `Icon.Minus` | löschen (mit Rückgängig) | „Alarm-Profil löschen“ | Rechteck | „Alarm-Profil löschen“ |
| `icons/lucide/power.svg` | `Icon.Power` | als deinstalliert markieren | Geräteliste im Admin-Dashboard | Ellipse + Linie | „Als deinstalliert markieren“ |
| `icons/lucide/eye.svg` | `Icon.Eye` | Passwort anzeigen | Install-Creator | zwei Ellipsen | „Passwort anzeigen“ |
| `icons/lucide/eye-off.svg` | `Icon.EyeOff` | Passwort verbergen | Install-Creator | Ellipsen + Linie | „Passwort anzeigen“ (Umschalter) |
| `icons/lucide/dices.svg` | `Icon.Dices` | zufällig erzeugen | Install-Creator, „Passwort generieren“ | zwei Rahmen mit Punkten | „Passwort generieren“ |
| `icons/lucide/copy.svg` | `Icon.Copy` | in die Zwischenablage kopieren | Install-Creator, „Passwort kopieren“ | zwei Rahmen | „Passwort kopieren“ |

## Farben (`xaml/Design.xaml`)

Jede Farbe gibt es als `…Color` (für `Color`-Eigenschaften) und als `…Brush`.

| Schlüssel | Wert | Bedeutung |
|---|---|---|
| `WindowBackground` | `#F5F5F7` | Fensterhintergrund |
| `CardBackground` | `#FFFFFF` | Karten, Tabellenzeilen |
| `SubtleBackground` | `#F4F6F6` | dezente Hinterlegung (Log-Feld, Schwellwert-Status im Alarm) |
| `TableAlternateRow` | `#FAFAFC` | jede zweite Tabellenzeile |
| `Hairline` | `#D2D2D7` | Trennlinien, Rahmen, Schalter „aus“ |
| `TextPrimary` | `#1D1D1F` | Haupttext |
| `TextSecondary` | `#6E6E73` | Nebentext, Icons in neutralen Buttons |
| `TextStatus` | `#34495E` | Statustext im Alarm-Popup |
| `Accent` | `#0071E3` | Akzent, primäre Buttons, Schalter „an“ |
| `AccentPressed` | `#005BBF` | Akzent gedrückt |
| `AccentLight` | `#D6E9FA` | „gerade zur Bearbeitung ausgewählt“ |
| `SecondaryButton` | `#E8E8ED` | sekundäre Buttons |
| `Danger` | `#FF3B30` | Fehler, abgelaufen, ungültig |
| `Success` | `#34C759` | Erfolg |
| `SuccessLight` | `#D6F5DD` | ganze Zeile „aktiv/zugeordnet“ |
| `Warning` | `#FF9500` | „läuft bald ab“ |
| `Alarm` | `#C0392B` | Alarm-Popup (Rahmen, Kopf), Abbrechen, Badge „Neu“ |
| `AlarmHeaderSubtext` | `#F5B7B1` | Untertitel im roten Alarm-Kopf |
| `OnMyWayButton` | `#27AE60` | Button „Bin unterwegs“ |
| `AlarmCloseButton` | `#2E86C1` | Button „Schließen“ im Alarm-Popup |
| `ExportButton` | `#2471A3` | Button „User-Installer exportieren“ |
| `SlateButton` | `#2C3E50` | Button „Empfängerliste übertragen“ |
| `AdminButton` | `#8E44AD` | Button „Admin-Dashboard öffnen“ |
| `ToastSuccessBackground` | `#DD1E8449` | Erfolgs-Toast (halbtransparent) |
| `ToastSuccessAccent` | `#1E8449` | Schrift weißer Buttons im Erfolgs-Toast |
| `ToastSuccessSubtext` | `#D5F5E3` | Nebentext und Schließen-Icon im Erfolgs-Toast |
| `BannerBackground` | `#DD2C3E50` | Sender-Status-Banner (halbtransparent) |
| `BannerSubtext` | `#D6DBDF` | Nebentext im Banner |
| `BannerClose` | `#BDC3C7` | Schließen-Icon im Banner |

Hinweis Barrierefreiheit: Einige Zustände werden bisher nur über Farbe unterschieden (z. B.
„grün = zugeordnet“ im Admin-Dashboard). Das widerspricht Teil A („Information nie nur über
Farbe“) und ist als eigener Fund gemeldet.

## Schrift

| Schlüssel | Wert | Bedeutung |
|---|---|---|
| `AppFontFamily` | `Segoe UI Variable Text, Segoe UI` | Standardschrift aller Fenster |
| – | `Consolas` | Monospace für Log-Ausgaben (direkt gesetzt, Windows-Systemschrift) |
