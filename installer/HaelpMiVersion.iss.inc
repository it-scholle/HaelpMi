; Setzt MyAppVersion aus <Version> in Directory.Build.props, der einzigen Quelle der
; Versionsnummer. Zeilensuche statt XML-Parser: <Version>...</Version> muss auf einer Zeile
; stehen, und vor dem Element darf "<Version>" nicht vorkommen (auch nicht im Kommentar).
; Die CI vergleicht das Ergebnis mit dem echten XML-Wert (.github/workflows/ci.yml).

#define VersionPropsPath AddBackslash(SourcePath) + "..\Directory.Build.props"
#if !FileExists(VersionPropsPath)
  #error Directory.Build.props nicht gefunden (installer-Ordner muss daneben liegen)
#endif

#define VersionPropsHandle
#define VersionPropsLine ""
#sub ReadVersionPropsLine
  #define VersionPropsLine = FileRead(VersionPropsHandle)
  #pragma message "DIAG Zeile: " + VersionPropsLine
#endsub
#for {VersionPropsHandle = FileOpen(VersionPropsPath); VersionPropsHandle && !FileEof(VersionPropsHandle) && Pos("<Version>", VersionPropsLine) == 0; ""} ReadVersionPropsLine
#if VersionPropsHandle
  #expr FileClose(VersionPropsHandle)
#endif

#define VersionStart Pos("<Version>", VersionPropsLine) + 9
#define MyAppVersion Copy(VersionPropsLine, VersionStart, Pos("</Version>", VersionPropsLine) - VersionStart)
#pragma message "MyAppVersion " + MyAppVersion + " aus " + VersionPropsPath
#if VersionStart == 9 || MyAppVersion == ""
  #error Version nicht ermittelt: kein <Version>-Element in Directory.Build.props gefunden
#endif
