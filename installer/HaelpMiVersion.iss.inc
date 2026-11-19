; Setzt MyAppVersion aus <Version> in Directory.Build.props, der einzigen Quelle der
; Versionsnummer. Zeilensuche statt XML-Parser: <Version>...</Version> muss auf einer Zeile
; stehen, und vor dem Element darf "<Version>" nicht vorkommen (auch nicht im Kommentar).
; Die CI vergleicht das Ergebnis mit dem echten XML-Wert (.github/workflows/ci.yml).

#define VersionPropsPath AddBackslash(SourcePath) + "..\Directory.Build.props"
#define MyAppVersion ""
#define VersionPropsHandle 0
#define VersionPropsLine ""
#define VersionStart 0
#define VersionEnd 0

#sub ReadVersionPropsLine
  #define VersionPropsLine = FileRead(VersionPropsHandle)
  #define VersionStart = Pos("<Version>", VersionPropsLine)
  #define VersionEnd = Pos("</Version>", VersionPropsLine)
  #define MyAppVersion = (VersionStart > 0 && VersionEnd > VersionStart) ? Copy(VersionPropsLine, VersionStart + 9, VersionEnd - VersionStart - 9) : ""
#endsub

#for {VersionPropsHandle = FileOpen(VersionPropsPath); VersionPropsHandle && !FileEof(VersionPropsHandle) && MyAppVersion == ""; 0} ReadVersionPropsLine

#if VersionPropsHandle
  #expr FileClose(VersionPropsHandle)
#endif

#if MyAppVersion == ""
  #error Version nicht ermittelt: kein <Version>-Element in Directory.Build.props gefunden
#endif
