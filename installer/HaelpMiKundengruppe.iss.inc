; Kunden-/Gruppen-ID (FR-35/FR-49), die Admin- und User-Installer verknüpft. Der Install-Creator
; setzt sie immer per "ISCC /DCustomerGroupId={guid} /DIsTestInstaller=true|false".
; Format wie ein .NET Guid ("D", 36 Zeichen, ohne Klammern), da System.Text.Json nur das liest.
;
; Ohne /DCustomerGroupId (manuelles ISCC) gilt nie die Null-GUID, denn deren Nachrichten verwirft
; MessageValidation.IsPlausible: Ein Test-Installer bekommt eine zufällige Gruppe, die einmal pro
; Arbeitskopie in installer\test-kundengruppe.txt (gitignored) entsteht. So finden sich Admin- und
; User-Installer aus demselben Checkout. Ein Kunden-Installer ohne Gruppe bricht ab.

#ifndef IsTestInstaller
  #define IsTestInstaller "true"
#endif

#ifndef CustomerGroupId
  #if IsTestInstaller != "true"
    #error Kunden-Installer (IsTestInstaller=false) ohne /DCustomerGroupId: bitte über den Install-Creator bauen oder /DCustomerGroupId=<GUID> angeben.
  #endif

  #define TestKundengruppePfad AddBackslash(SourcePath) + "test-kundengruppe.txt"
  #if !FileExists(TestKundengruppePfad)
    ; Arbeitsverzeichnis installer\, damit der Skriptpfad keine Anführungszeichen braucht.
    #define TestKundengruppeExit Exec("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File test-kundengruppe-erzeugen.ps1", SourcePath, 1, 0)
    #if TestKundengruppeExit != 0
      #error test-kundengruppe-erzeugen.ps1 ist fehlgeschlagen; installer\test-kundengruppe.txt von Hand mit einer GUID anlegen.
    #endif
  #endif

  #define TestKundengruppeHandle FileOpen(TestKundengruppePfad)
  #if !TestKundengruppeHandle
    #error installer\test-kundengruppe.txt ist nicht lesbar.
  #endif
  #define CustomerGroupId Trim(FileRead(TestKundengruppeHandle))
  #expr FileClose(TestKundengruppeHandle)

  #if Len(CustomerGroupId) != 36 || CustomerGroupId == "00000000-0000-0000-0000-000000000000"
    #error installer\test-kundengruppe.txt enthält keine gültige GUID; Datei löschen, sie wird beim nächsten Build neu erzeugt.
  #endif
  #pragma message "Test-Kundengruppe " + CustomerGroupId + " aus " + TestKundengruppePfad
#endif
